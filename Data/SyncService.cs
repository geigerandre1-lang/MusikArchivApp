using System;
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
            catch (Exception ex)
            {
                return (false, $"Verbindung fehlgeschlagen: {ex.Message}");
            }
        }

        public async Task<(bool ok, string message)> PushAsync(SyncConfig config, CancellationToken cancellationToken = default)
        {
            var payload = await syncRepository.BuildPushPayloadAsync().ConfigureAwait(false);
            payload.ClientLastSyncAt = config.LastSyncAt;
            payload.WebViewPassword = config.WebViewPassword;

            using var request = CreateRequest(HttpMethod.Post, config, "/api/sync/push");
            request.Content = JsonContent.Create(payload, options: JsonOptions);

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return (false, $"Upload fehlgeschlagen ({(int)response.StatusCode}): {body}");
            }

            config.LastSyncAt = DateTime.UtcNow;
            await SyncConfigStore.SaveAsync(config).ConfigureAwait(false);
            return (true, $"Upload abgeschlossen ({payload.Pieces.Count} Stücke, {payload.Sheets.Count} Notendateien).");
        }

        public async Task<(bool ok, string message)> PullAsync(SyncConfig config, CancellationToken cancellationToken = default)
        {
            var since = config.LastSyncAt?.ToString("o") ?? string.Empty;
            using var request = CreateRequest(HttpMethod.Get, config, $"/api/sync/pull?since={Uri.EscapeDataString(since)}");

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return (false, $"Download fehlgeschlagen ({(int)response.StatusCode}): {body}");
            }

            var payload = await response.Content.ReadFromJsonAsync<SyncPullResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (payload == null)
            {
                return (false, "Leere Server-Antwort.");
            }

            await syncRepository.ApplyPullPayloadAsync(payload).ConfigureAwait(false);

            config.LastSyncAt = payload.ServerTime == default ? DateTime.UtcNow : payload.ServerTime;
            await SyncConfigStore.SaveAsync(config).ConfigureAwait(false);
            return (true, $"Download abgeschlossen ({payload.Pieces.Count} Stücke, {payload.Sheets.Count} Notendateien).");
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
    }
}
