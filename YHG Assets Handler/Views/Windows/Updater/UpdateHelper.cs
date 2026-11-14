using AssetsHandler.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using YHG_Assets_Handler.Utils;

namespace YHG_Assets_Handler.Views.Windows.Updater
{
    public class ReleasePackage
    {
        public string Name { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public long SizeInBytes { get; set; }
        public string FormattedSize => $"{SizeInBytes / (1024.0 * 1024.0):F1} MB";
    }

    public class UpdateCheckResult
    {
        public bool IsNew { get; set; }
        public string Version { get; set; } = "1.0.0";
        public string Changelog { get; set; } = "";
        public ReleasePackage? StandalonePackage { get; set; }
        public ReleasePackage? LightweightPackage { get; set; }
    }

    public class UpdateHelper
    {
        private static readonly HttpClient client = new HttpClient();
        private const string API_URL = "https://api.github.com/repos/yhgindian/yhg-assets-handler/releases/latest";

        public static string PreviousVersion => Settings.Instance["PreviousVersion"].AsString();
        public static string PreviousChangelog => Settings.Instance["PreviousChangelog"].AsString();
        public static string UpdateStatus => Settings.Instance["UpdateStatus"].AsString();
        public static string FilePath => Settings.Instance["FilePath"].AsString();
        public static string AvailableVersion => Settings.Instance["AvailableVersion"].AsString();
        public static string AvailableChangelog => Settings.Instance["AvailableChangelog"].AsString();

        static UpdateHelper()
        {
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                client.DefaultRequestHeaders.Add("User-Agent", "YHG-Updater-Agent");
        }

        public static void SaveSettings(string prevVer, string prevLog, string status, string fPath, string availVer, string availLog)
        {
            try
            {
                Settings.Instance["PreviousVersion"] = prevVer;
                Settings.Instance["PreviousChangelog"] = prevLog;
                Settings.Instance["UpdateStatus"] = status;
                Settings.Instance["FilePath"] = fPath;
                Settings.Instance["AvailableVersion"] = availVer;
                Settings.Instance["AvailableChangelog"] = availLog;
                Settings.Instance.SaveImmediate();
            }
            catch { /* Protection */ }
        }

        public static void VerifyUpdateLifecycle(string currentVersion)
        {
            string staticCurrent = currentVersion.Replace("v", "").Trim();
            string staticAvailable = AvailableVersion.Replace("v", "").Trim();

            if (!string.IsNullOrEmpty(AvailableVersion) && UpdateStatus == "DownloadedButNotInstalled")
            {
                try
                {
                    Version current = new Version(staticCurrent);
                    Version target = new Version(staticAvailable);

                    if (current >= target)
                    {
                        SaveSettings(currentVersion, AvailableChangelog, "Updated", "", "", "");
                        MessageBox.Show($"Successfully updated to v{currentVersion}!\nEnjoy the latest features.", "YHG Core Engine", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }
                catch { /* Version format mismatch fail-safe */ }
            }

            if (UpdateStatus == "DownloadedButNotInstalled" && !string.IsNullOrEmpty(FilePath))
            {
                if (File.Exists(FilePath))
                {
                    bool? dr = ConfirmationDialog.Show(
                        title: "Pending Update Found",
                        message: $"Update Package (v{AvailableVersion}) download ho chuka hai par installation pending hai.\n\n" +
                                 $"Kya aap update archive open karke application update karne ke liye abhi band karna chahte hain?");

                    if (dr == true)
                    {
                        FileUtils.OpenInExplorer(FilePath);
                        Application.Current.Shutdown();
                        Environment.Exit(0);
                    }
                }
                else
                {
                    SaveSettings(PreviousVersion, PreviousChangelog, "Idle", "", "", "");
                }
            }
        }


        public static Task<UpdateCheckResult> IsUpdateAvailableAsync()
        {
            return IsUpdateAvailableAsync(GetCurrentVersion());
        }

        public static async Task<UpdateCheckResult> IsUpdateAvailableAsync(string currentVersion)
        {
            var result = new UpdateCheckResult();

            try
            {
                var response = await client.GetStringAsync(API_URL);

                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                string latestTag = root.GetProperty("tag_name").GetString() ?? "1.0.0";

                result.Version = latestTag;
                result.Changelog = root.GetProperty("body").GetString()
                    ?? "No changelog provided.";

                if (root.TryGetProperty("assets", out var assets) &&
                    assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? "";
                        string downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                        long size = asset.GetProperty("size").GetInt64();

                        var package = new ReleasePackage
                        {
                            Name = name,
                            DownloadUrl = downloadUrl,
                            SizeInBytes = size
                        };

                        if (name.Contains("Standalone", StringComparison.OrdinalIgnoreCase))
                            result.StandalonePackage = package;

                        else if (name.Contains("Lightweight", StringComparison.OrdinalIgnoreCase))
                            result.LightweightPackage = package;
                    }
                }

                Version current = new Version(currentVersion.TrimStart('v', 'V').Trim());
                Version latest = new Version(latestTag.TrimStart('v', 'V').Trim());

                result.IsNew = latest > current;

                return result;
            }
            catch
            {
                result.IsNew = false;
                result.Changelog = "Failed to establish network pipeline with GitHub API.";

                return result;
            }
        }


        public static async Task<bool> DownloadUpdateAsync(string url, string savePath, Action<int> progressCallback)
        {
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    if (totalBytes > 0 && progressCallback != null)
                    {
                        int percentage = (int)((totalBytesRead * 100) / totalBytes);
                        progressCallback(percentage);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GetCurrentVersion() => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    }
}