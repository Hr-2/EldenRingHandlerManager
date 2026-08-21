using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ERHandlerManager.Services
{
    public class UpdateInfo
    {
        public bool HasUpdate { get; set; }
        public string CurrentVersion { get; set; } = "";
        public string LatestVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ReleaseNotesUrl { get; set; } = "";
    }

    public static class UpdateService
    {
        private const string RepoOwner = "Hr-2";
        private const string RepoName = "EldenRingHandlerManager";
        private const string ReleaseApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

        /// <summary>Queries GitHub for the latest release and compares it to the running version.</summary>
        public static async Task<UpdateInfo> CheckForUpdateAsync()
        {
            var info = new UpdateInfo { CurrentVersion = AppInfo.Version };

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ERHandlerManager");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                client.Timeout = TimeSpan.FromSeconds(20);

                var json = await client.GetStringAsync(ReleaseApiUrl);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tagName = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? "" : "";
                info.LatestVersion = tagName.TrimStart('v');
                info.ReleaseNotesUrl = root.TryGetProperty("html_url", out var html) ? html.GetString() ?? "" : "";

                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (name.Equals("EldenRingHandlerManager.zip", StringComparison.OrdinalIgnoreCase))
                        {
                            info.DownloadUrl = asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() ?? "" : "";
                            break;
                        }
                    }
                }

                if (Version.TryParse(info.LatestVersion, out var latest) &&
                    Version.TryParse(info.CurrentVersion, out var current))
                {
                    info.HasUpdate = latest > current;
                }
            }
            catch
            {
                // Offline, rate-limited, or GitHub changed — treat as "no update found".
            }

            return info;
        }

        /// <summary>
        /// Downloads the release zip, extracts it, then hands off to a hidden batch
        /// helper that waits for this process to exit, swaps the files, and relaunches.
        /// </summary>
        public static async Task DownloadAndApplyAsync(UpdateInfo info, IProgress<(int percent, string status)>? progress = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(info.DownloadUrl))
                throw new InvalidOperationException("No update asset found on GitHub.");

            var appDir = Path.GetDirectoryName(typeof(UpdateService).Assembly.Location) ?? ".";
            var tempDir = Path.Combine(Path.GetTempPath(), "ERHandlerManager_update");
            var zipPath = Path.Combine(tempDir, "update.zip");
            var extractDir = Path.Combine(tempDir, "extracted");

            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(extractDir);

                progress?.Report((0, "Downloading update..."));

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ERHandlerManager");
                client.Timeout = TimeSpan.FromMinutes(15);

                using (var response = await client.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1;

                    await using (var content = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    await using (var fs = File.Create(zipPath))
                    {
                        var buffer = new byte[81920];
                        long readBytes = 0;
                        int lastPct = -1;
                        int bytesRead;
                        while ((bytesRead = await content.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await fs.WriteAsync(buffer, 0, bytesRead, ct).ConfigureAwait(false);
                            readBytes += bytesRead;
                            if (totalBytes > 0)
                            {
                                var pct = (int)(readBytes * 100 / totalBytes);
                                if (pct != lastPct)
                                {
                                    lastPct = pct;
                                    progress?.Report((pct, $"Downloading update... {pct}%"));
                                }
                            }
                        }
                    }
                }

                progress?.Report((95, "Extracting update..."));

                ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

                progress?.Report((99, "Applying update..."));

                var exeName = Process.GetCurrentProcess().ProcessName + ".exe";
                var pid = Environment.ProcessId;
                var batchPath = Path.Combine(tempDir, "update.bat");

                var batch = $@"@echo off
setlocal
set ""APP_DIR={appDir}""
set ""STAGE_DIR={extractDir}""
set ""PID={pid}""
set ""EXE={exeName}""

:wait
tasklist /FI ""PID eq %PID%"" 2>NUL | find ""%PID%"" >NUL
if not errorlevel 1 (
    timeout /t 1 /nobreak >NUL
    goto wait
)

robocopy ""%STAGE_DIR%"" ""%APP_DIR%"" /E /IS /R:3 /W:2 >NUL
if errorlevel 8 (
    echo ERROR: Failed to copy update files.
    pause
    exit /b 1
)

rmdir /S /Q ""%STAGE_DIR%"" 2>NUL
del ""%~dp0update.zip"" 2>NUL
del ""%~f0"" 2>NUL

start """" ""%APP_DIR%\%EXE%""
";

                File.WriteAllText(batchPath, batch);

                var psi = new ProcessStartInfo
                {
                    FileName = batchPath,
                    WorkingDirectory = Path.GetDirectoryName(batchPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);

                progress?.Report((100, "Update ready. Restarting..."));
            }
            catch (Exception ex)
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { }
                throw new InvalidOperationException("Update failed: " + ex.Message, ex);
            }
        }
    }
}
