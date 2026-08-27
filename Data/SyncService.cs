using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MusikArchivApp.Models;

namespace MusikArchivApp.Data
{
    public class SyncService
    {
        private const int PrepareWeightPercent = 25;
        private const int SerializeWeightPercent = 5;
        private const int UploadWeightPercent = 70;
        private const int DownloadWeightPercent = 85;
        private const int ApplyWeightPercent = 15;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly SyncRepository syncRepository;
        private readonly HttpClient httpClient;

        public SyncService(SyncRepository syncRepository, HttpClient? httpClient = null)
        {
            this.syncRepository = syncRepository;
            this.httpClient = httpClient ?? new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
        }

        public async Task<(bool ok, string message)> TestConnectionAsync(SyncConfig config, CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = CreateRequest(HttpMethod.Get, config, "/api/health");
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return (false, $"Server antwortete mit {(int)response.StatusCode}.");
                }

                var health = await response.Content.ReadFromJsonAsync<SyncHealthResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                return health?.Ok == true
                    ? (true, $"Verbindung OK (API {health.Version ?? "?"}).")
                    : (false, "Server antwortete, meldet aber keinen OK-Status.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, $"Verbindung fehlgeschlagen: {ex.Message}");
            }
        }

        public async Task<(bool ok, string message)> PushAsync(
            SyncConfig config,
            CancellationToken cancellationToken = default,
            IProgress<SyncProgressReport>? progress = null)
        {
            Report(progress, "Daten vorbereiten", 0);

            var payload = await syncRepository.BuildPushPayloadAsync(
                new Progress<(int current, int total)>(value =>
                {
                    var percent = ScalePercent(value.current, value.total, 0, PrepareWeightPercent);
                    Report(progress, "Daten vorbereiten", percent);
                }),
                cancellationToken).ConfigureAwait(false);

            payload.ClientLastSyncAt = config.LastSyncAt;
            if (WebPasswordPolicy.TryValidate(config.WebViewPassword, out _))
            {
                payload.WebViewPassword = config.WebViewPassword;
            }

            Report(progress, "JSON serialisieren", PrepareWeightPercent);
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            Report(progress, "JSON serialisieren", PrepareWeightPercent + SerializeWeightPercent, jsonBytes.Length, jsonBytes.Length);

            using var request = CreateRequest(HttpMethod.Post, config, "/api/sync/push");
            request.Content = new ProgressByteArrayContent(
                jsonBytes,
                "application/json",
                (sent, total, rate) =>
                {
                    var uploadPercent = total > 0 ? (int)(sent * UploadWeightPercent / total) : 0;
                    var percent = PrepareWeightPercent + SerializeWeightPercent + uploadPercent;
                    Report(progress, "Upload", Math.Min(percent, 99), sent, total, rate);
                });

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return (false, $"Upload fehlgeschlagen ({(int)response.StatusCode}): {body}");
            }

            var pushResult = await response.Content.ReadFromJsonAsync<SyncPushResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            config.LastSyncAt = DateTime.UtcNow;
            await SyncConfigStore.SaveAsync(config).ConfigureAwait(false);
            Report(progress, "Upload abgeschlossen", 100, jsonBytes.Length, jsonBytes.Length);

            var message = $"Upload abgeschlossen ({payload.Pieces.Count} Stücke, {payload.Sheets.Count} Notendateien).";
            if (!WebPasswordPolicy.TryValidate(config.WebViewPassword, out var policyError))
            {
                message += $" Web-Passwort nicht übertragen: {policyError}";
            }
            else if (!string.IsNullOrWhiteSpace(pushResult?.PasswordWarning))
            {
                message += $" {pushResult.PasswordWarning}";
            }

            return (true, message);
        }

        public async Task<(bool ok, string message)> PullAsync(
            SyncConfig config,
            CancellationToken cancellationToken = default,
            IProgress<SyncProgressReport>? progress = null)
        {
            var since = config.LastSyncAt?.ToString("o") ?? string.Empty;
            using var request = CreateRequest(HttpMethod.Get, config, $"/api/sync/pull?since={Uri.EscapeDataString(since)}");

            Report(progress, "Download", 0);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return (false, $"Download fehlgeschlagen ({(int)response.StatusCode}): {body}");
            }

            var totalBytes = response.Content.Headers.ContentLength;
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var payloadStream = new MemoryStream();
            var buffer = new byte[81_920];
            long downloadedBytes = 0;
            var stopwatch = Stopwatch.StartNew();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await payloadStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                downloadedBytes += read;

                var percent = totalBytes is > 0
                    ? ScalePercent(downloadedBytes, totalBytes.Value, 0, DownloadWeightPercent)
                    : Math.Min(DownloadWeightPercent - 1, (int)(downloadedBytes / (1024 * 1024)));

                var rate = stopwatch.Elapsed.TotalSeconds > 0.05 ? downloadedBytes / stopwatch.Elapsed.TotalSeconds : (double?)null;
                Report(progress, "Download", percent, downloadedBytes, totalBytes, rate);
            }

            payloadStream.Position = 0;
            var payload = await JsonSerializer.DeserializeAsync<SyncPullResponse>(payloadStream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (payload == null)
            {
                return (false, "Leere Server-Antwort.");
            }

            Report(progress, "Daten übernehmen", DownloadWeightPercent, downloadedBytes, totalBytes ?? downloadedBytes);
            await syncRepository.ApplyPullPayloadAsync(
                payload,
                new Progress<(int current, int total)>(value =>
                {
                    var percent = DownloadWeightPercent + ScalePercent(value.current, value.total, 0, ApplyWeightPercent);
                    Report(progress, "Daten übernehmen", Math.Min(percent, 99));
                }),
                cancellationToken).ConfigureAwait(false);

            config.LastSyncAt = payload.ServerTime == default ? DateTime.UtcNow : payload.ServerTime;
            await SyncConfigStore.SaveAsync(config).ConfigureAwait(false);
            Report(progress, "Download abgeschlossen", 100, downloadedBytes, totalBytes ?? downloadedBytes);
            return (true, $"Download abgeschlossen ({payload.Pieces.Count} Stücke, {payload.Sheets.Count} Notendateien).");
        }

        public async Task<(bool ok, string message)> WipeAsync(SyncConfig config, CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = CreateRequest(HttpMethod.Post, config, "/api/sync/wipe");
                request.Content = JsonContent.Create(new { });
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return (false, $"Löschen fehlgeschlagen ({(int)response.StatusCode}): {body}");
                }

                var result = await response.Content.ReadFromJsonAsync<SyncWipeResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                config.LastSyncAt = null;
                await SyncConfigStore.SaveAsync(config).ConfigureAwait(false);

                return (
                    true,
                    $"Web-Datenbank geleert ({result?.Pieces ?? 0} Stücke, {result?.Sheets ?? 0} Notendateien, {result?.VaultFiles ?? 0} Tresor-Dateien). Das Web-Passwort bleibt erhalten. Als Nächstes zum Server hochladen.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, $"Löschen fehlgeschlagen: {ex.Message}");
            }
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, SyncConfig config, string path)
        {
            var baseUrl = config.ServerUrl.TrimEnd('/');
            var request = new HttpRequestMessage(method, $"{baseUrl}{path}");
            if (!string.IsNullOrWhiteSpace(config.ApiKey))
            {
                request.Headers.Add("X-Api-Key", config.ApiKey);
            }

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        private static int ScalePercent(long current, long total, int startPercent, int spanPercent)
        {
            if (total <= 0)
            {
                return startPercent;
            }

            return startPercent + (int)(current * spanPercent / total);
        }

        private static int ScalePercent(int current, int total, int startPercent, int spanPercent)
        {
            return ScalePercent((long)current, total, startPercent, spanPercent);
        }

        private static void Report(
            IProgress<SyncProgressReport>? progress,
            string phaseLabel,
            int percentComplete,
            long bytesTransferred = 0,
            long? totalBytes = null,
            double? bytesPerSecond = null)
        {
            progress?.Report(new SyncProgressReport
            {
                PhaseLabel = phaseLabel,
                PercentComplete = Math.Clamp(percentComplete, 0, 100),
                BytesTransferred = bytesTransferred,
                TotalBytes = totalBytes,
                BytesPerSecond = bytesPerSecond
            });
        }
    }
}
