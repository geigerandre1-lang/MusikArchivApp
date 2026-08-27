using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusikArchivApp.Data
{
    public sealed class AppUpdateInfo
    {
        public string Version { get; init; } = string.Empty;
        public string Tag { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string DisplayLabel { get; init; } = string.Empty;
        public string DownloadUrl { get; init; } = string.Empty;
        public string AssetName { get; init; } = string.Empty;
        public long Size { get; init; }
        public bool IsNewer { get; init; }
    }

    public static class AppUpdateService
    {
        private const string Owner = "geigerandre1-lang";
        private const string Repo = "MusikArchivApp";
        private const string ReleasesUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=15";

        private static readonly HttpClient Http = CreateClient();

        public static AppUpdateInfo? Latest { get; private set; }

        public static IReadOnlyList<AppUpdateInfo> Releases { get; private set; } = Array.Empty<AppUpdateInfo>();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MusikArchivApp", AppVersion.Value));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        public static bool CanApplyInPlace()
        {
            var exeDir = GetInstallDirectory();
            if (exeDir.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || exeDir.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                var probe = Path.Combine(exeDir, $".update-probe-{Guid.NewGuid():N}");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<AppUpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
        {
            using var response = await Http.GetAsync(ReleasesUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(GitHubError(response.StatusCode));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                Releases = Array.Empty<AppUpdateInfo>();
                Latest = null;
                return null;
            }

            var mapped = new List<AppUpdateInfo>();
            foreach (var release in document.RootElement.EnumerateArray())
            {
                var info = TryMapRelease(release);
                if (info != null)
                {
                    mapped.Add(info);
                }
            }

            mapped.Sort((left, right) => CompareVersions(right.Version, left.Version));
            var unique = new List<AppUpdateInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in mapped)
            {
                if (!seen.Add(info.Version))
                {
                    continue;
                }

                unique.Add(info);
                if (unique.Count >= 12)
                {
                    break;
                }
            }

            Releases = unique;
            Latest = unique.Count > 0 ? unique[0] : null;
            return Latest;
        }

        public static async Task DownloadAndApplyAsync(
            AppUpdateInfo update,
            IProgress<(long received, long total)>? progress,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(update.DownloadUrl))
            {
                throw new InvalidOperationException("Kein Download-Link für diese Version.");
            }

            if (!CanApplyInPlace())
            {
                throw new InvalidOperationException(
                    "Update nur in der installierten oder portable App, nicht aus dem Entwicklungsordner.");
            }

            var workDir = Path.Combine(Path.GetTempPath(), "MusikArchivApp-update");
            Directory.CreateDirectory(workDir);
            var zipPath = Path.Combine(workDir, string.IsNullOrWhiteSpace(update.AssetName) ? "update.zip" : update.AssetName);
            var extractDir = Path.Combine(workDir, "extracted");

            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, recursive: true);
            }

            Directory.CreateDirectory(extractDir);

            await DownloadFileAsync(update.DownloadUrl, zipPath, progress, cancellationToken).ConfigureAwait(false);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            var extractedExe = Directory.EnumerateFiles(extractDir, "MusikArchivApp.exe", SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .FirstOrDefault();
            if (extractedExe == null)
            {
                throw new InvalidOperationException("Im Update-Paket fehlt MusikArchivApp.exe.");
            }

            var sourceDir = Path.GetDirectoryName(extractedExe)!;
            var targetDir = GetInstallDirectory();
            var exePath = Path.Combine(targetDir, "MusikArchivApp.exe");
            var scriptPath = WriteApplyScript();
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.EnvironmentVariables["MUSIKARCHIV_UPDATE_PID"] = Environment.ProcessId.ToString();
            startInfo.EnvironmentVariables["MUSIKARCHIV_UPDATE_SOURCE"] = sourceDir;
            startInfo.EnvironmentVariables["MUSIKARCHIV_UPDATE_TARGET"] = targetDir;
            startInfo.EnvironmentVariables["MUSIKARCHIV_UPDATE_EXE"] = exePath;
            Process.Start(startInfo);
        }

        private static async Task DownloadFileAsync(
            string url,
            string destPath,
            IProgress<(long received, long total)>? progress,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? 0;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                progress?.Report((received, total));
            }
        }

        private static string GetInstallDirectory()
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var dir = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    return dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
            }

            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string WriteApplyScript()
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), $"musikarchiv-apply-{Environment.ProcessId}.ps1");
            const string script = @"
$log = Join-Path $env:TEMP 'musikarchiv-update.log'
function Write-UpdateLog([string]$message) {
  Add-Content -LiteralPath $log -Value ((Get-Date -Format 'o') + ' ' + $message)
}

$pidToWait = 0
[void][int]::TryParse($env:MUSIKARCHIV_UPDATE_PID, [ref]$pidToWait)
$source = $env:MUSIKARCHIV_UPDATE_SOURCE
$target = $env:MUSIKARCHIV_UPDATE_TARGET
$exe = $env:MUSIKARCHIV_UPDATE_EXE
Write-UpdateLog ('start pid=' + $pidToWait + ' source=' + $source + ' target=' + $target)

if (-not $source -or -not $target -or -not $exe) {
  Write-UpdateLog 'missing environment variables'
  exit 1
}

while ($pidToWait -gt 0 -and (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue)) {
  Start-Sleep -Milliseconds 400
}
Start-Sleep -Milliseconds 1000

function Copy-Replace([string]$src, [string]$dst) {
  if ((Get-Item -LiteralPath $src).PSIsContainer) {
    if (-not (Test-Path -LiteralPath $dst)) {
      New-Item -ItemType Directory -Path $dst | Out-Null
    }
    Get-ChildItem -LiteralPath $src -Force | ForEach-Object {
      if ($_.Name -ieq 'data') { return }
      Copy-Replace $_.FullName (Join-Path $dst $_.Name)
    }
    return
  }

  $tmp = $dst + '.new'
  $attempt = 0
  while ($attempt -lt 40) {
    try {
      Copy-Item -LiteralPath $src -Destination $tmp -Force
      if (Test-Path -LiteralPath $dst) {
        Remove-Item -LiteralPath $dst -Force -ErrorAction Stop
      }
      Move-Item -LiteralPath $tmp -Destination $dst -Force
      return
    } catch {
      Start-Sleep -Milliseconds 500
      $attempt++
    }
  }
  throw ('cannot replace ' + $dst)
}

try {
  Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
    if ($_.Name -ieq 'data') { return }
    Copy-Replace $_.FullName (Join-Path $target $_.Name)
  }
  Write-UpdateLog 'copy ok'
  Start-Process -FilePath $exe -WorkingDirectory $target
} catch {
  Write-UpdateLog ('copy failed: ' + $_)
  try {
    Start-Process -FilePath $exe -WorkingDirectory $target
  } catch {}
  $wshell = New-Object -ComObject Wscript.Shell
  [void]$wshell.Popup('Update konnte nicht vollstaendig angewendet werden. Die vorherige Version wurde gestartet. Log: ' + $log, 0, 'Musikarchiv', 16)
}

Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
";
            File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return scriptPath;
        }

        private static AppUpdateInfo? TryMapRelease(JsonElement release)
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            {
                return null;
            }

            if (release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
            {
                return null;
            }

            var tag = release.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            var version = ParseVersion(tag);
            if (version == null)
            {
                return null;
            }

            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var asset = PickAsset(assets);
            if (asset == null)
            {
                return null;
            }

            var newer = CompareVersions(version, AppVersion.Current) > 0;
            var current = CompareVersions(version, AppVersion.Current) == 0;
            var suffix = newer ? " — neuer" : current ? " — aktuell" : string.Empty;
            return new AppUpdateInfo
            {
                Version = version,
                Tag = tag ?? $"v{version}",
                Name = release.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? version : version,
                DisplayLabel = version + suffix,
                DownloadUrl = asset.Value.url,
                AssetName = asset.Value.name,
                Size = asset.Value.size,
                IsNewer = newer
            };
        }

        private static (string name, string url, long size)? PickAsset(JsonElement assets)
        {
            (string name, string url, long size)? portable = null;
            (string name, string url, long size)? winZip = null;
            (string name, string url, long size)? anyZip = null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                var url = asset.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() ?? string.Empty : string.Empty;
                var size = asset.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var n) ? n : 0;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var lower = name.ToLowerInvariant();
                if (!lower.EndsWith(".zip", StringComparison.Ordinal))
                {
                    continue;
                }

                var mapped = (name, url, size);
                anyZip ??= mapped;
                if (lower.Contains("win-x64"))
                {
                    winZip ??= mapped;
                }

                if (lower.Contains("portable") && lower.Contains("win-x64"))
                {
                    portable ??= mapped;
                }
            }

            return portable ?? winZip ?? anyZip;
        }

        public static int CompareVersions(string left, string right)
        {
            var a = VersionParts(left);
            var b = VersionParts(right);
            for (var i = 0; i < 3; i++)
            {
                var diff = a[i] - b[i];
                if (diff != 0)
                {
                    return diff;
                }
            }

            return 0;
        }

        private static int[] VersionParts(string version)
        {
            var core = (version ?? string.Empty).Trim().TrimStart('v', 'V').Split('-')[0];
            var parts = core.Split('.').Select(part => int.TryParse(part, out var n) ? n : 0).ToList();
            while (parts.Count < 3)
            {
                parts.Add(0);
            }

            return parts.Take(3).ToArray();
        }

        private static string? ParseVersion(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag) || tag.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var version = tag.Trim().TrimStart('v', 'V');
            var core = version.Split('-')[0];
            return core.Split('.').All(part => int.TryParse(part, out _)) ? version : null;
        }

        private static string GitHubError(System.Net.HttpStatusCode status)
        {
            var code = (int)status;
            if (code == 403)
            {
                return "GitHub-Anfragelimit erreicht. Bitte später erneut prüfen.";
            }

            return $"GitHub antwortet nicht (HTTP {code}).";
        }
    }
}
