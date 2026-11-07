using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AssetsHandler.Core.NotificationManager;
using AssetsTools.NET;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Utils;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.Views;
using YHG_Assets_Handler.Workspace.UnityAssets;
using YHG_Assets_Handler.Workspace.UnityAssets.Schemas;

namespace YHG_Assets_Handler.Editor.Unity
{
    public static class DataEditor
    {
        private static readonly ConcurrentDictionary<string, (AssetContainer container, UnityAssetWorkspace workspace)> FileMap = new();
        private static readonly ConcurrentDictionary<string, FileSystemWatcher> Watchers = new();
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> DebounceTokens = new();

        public static void Reset()
        {
            foreach (var watcher in Watchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            Watchers.Clear();
            FileMap.Clear();

            foreach (var cts in DebounceTokens.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            DebounceTokens.Clear();
        }

        public static async Task OpenEditorAsync(List<AssetContainer> containers, UnityAssetWorkspace workspace)
        {
            if (containers == null || containers.Count == 0)
                return;

            // Session isolated TempFiles folder: %LocalAppData%/{AppName}/Sessions/{SessionId}/TempFiles/LiveEditor
            string dir = Path.Combine(workspace.TempFilesDirectory, "LiveEditor");
            AppPathResolver.EnsureDirectory(dir);

            var pathsToOpen = new List<string>();

            foreach (var container in containers)
            {
                if (container.FinalSize > 40_000_000)
                {
                    string sizeStr = FileUtils.GetFormattedByteSize(container.FinalSize);
                    var result = ConfirmationDialog.Show("Want to continue?", $"File is too big ({sizeStr}). Do you still want to open it?");
                    if (!result) continue;
                }

                AssetNameUtils.GetDisplayNameFast(workspace, container, false, out var name, out _);
                string fileName = AssetNameUtils.getSaveFileName(container, name);

                // Temp path inside Session's TempFiles folder
                string path = Path.Combine(dir, $"Asset_{container.PathId}_{fileName}.json");

                RemoveWatcher(path);

                try
                {
                    AssetTypeValueField? baseField = null;
                    var fileInst = container.FileInstance;

                    if (fileInst != null)
                    {
                        lock (fileInst.LockReader)
                        {
                            baseField = workspace.GetBaseField(container);
                        }
                    }

                    if (baseField == null)
                    {
                        throw new InvalidOperationException($"Could not inspect type fields for asset {fileName}.");
                    }

                    // 1. Schema Generation / Retrieval
                    string? schemaUri = null;
                    try
                    {
                        string unityVersion = fileInst?.file?.Metadata?.UnityVersion ?? "Universal";
                        string? scriptId = null;

                        if (baseField.TemplateField.Type == "MonoBehaviour")
                        {
                            var scriptPtr = baseField["m_Script"];
                            if (scriptPtr != null && !scriptPtr.IsDummy)
                            {
                                long scriptPathId = scriptPtr["m_PathID"].AsLong;
                                int scriptFileId = scriptPtr["m_FileID"].AsInt;
                                scriptId = $"{name}_Script_{scriptFileId}_{scriptPathId}";
                            }
                            else
                            {
                                scriptId = $"{name}_{container.PathId}";
                            }
                        }

                        schemaUri = RuntimeJsonSchemaGenerator.EnsureAndGetSchemaUri(
                            unityVersion,
                            baseField.TemplateField,
                            scriptId
                        );
                    }
                    catch (Exception ex)
                    {
                        Status.Write($"[Schema] Schema generation skipped: {ex.Message}", StatusType.Warning);
                    }

                    // 2. Dump karte waqt schema URI pass karo
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                    using (var sw = new StreamWriter(fs, Encoding.UTF8, 8192))
                    using (var exporter = new AssetImportExport(sw, leaveOpen: true))
                    {
                        exporter.DumpJsonAsset(baseField, indent: true, schemaUri: schemaUri);
                    }

                    FileMap[path] = (container, workspace);
                    pathsToOpen.Add(path);

                    SetupFileWatcher(path);
                }
                catch (Exception ex)
                {
                    Status.Write($"[Editor] Export failed for {fileName}: {ex.Message}", StatusType.Error);
                    NotificationManager.Error($"Export failed: {ex.Message}", fileName);
                }
            }

            if (pathsToOpen.Count > 0)
            {
                string joinedArgs = string.Join(" ", pathsToOpen.ConvertAll(p => $"\"{p}\""));
                string preferred = Settings.Instance["PreferredEditorExe"].AsString();

                bool opened = false;
                if (!string.IsNullOrEmpty(preferred) && File.Exists(preferred))
                {
                    opened = TryOpenWith(preferred, joinedArgs);
                }

                if (!opened)
                {
                    if (TryOpenWith("code", joinedArgs))
                    {
                        opened = true;
                        Settings.Instance.Set("PreferredEditorExe", "code");
                    }
                    else if (TryOpenWith("notepad++", joinedArgs))
                    {
                        opened = true;
                        Settings.Instance.Set("PreferredEditorExe", "notepad++");
                    }
                    else if (TryOpenWith("notepad", joinedArgs))
                    {
                        opened = true;
                        Settings.Instance.Set("PreferredEditorExe", "notepad");
                    }
                }

                if (opened)
                {
                    Status.Write($"[Editor] Opened {pathsToOpen.Count} file(s) for live editing.", StatusType.Success);
                }
                else
                {
                    NotificationManager.Error("No valid code editor found to launch.", "Editor Error");
                }
            }
        }

        private static void SetupFileWatcher(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

            var watcher = new FileSystemWatcher(directory)
            {
                Filter = Path.GetFileName(path),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };

            void OnChanged(object sender, FileSystemEventArgs e) => TriggerDebouncedReload(path);

            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Renamed += OnChanged;
            watcher.Deleted += (sender, e) => RemoveWatcher(path);

            watcher.EnableRaisingEvents = true;
            Watchers[path] = watcher;
        }

        private static void TriggerDebouncedReload(string path)
        {
            if (DebounceTokens.TryGetValue(path, out var oldCts))
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }

            var cts = new CancellationTokenSource();
            DebounceTokens[path] = cts;

            Task.Delay(350, cts.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    ProcessFileChanges(path);
                }
            }, TaskScheduler.Default);
        }

        private static void ProcessFileChanges(string path)
        {
            if (!FileMap.TryGetValue(path, out var pair))
                return;

            var (container, workspace) = pair;
            string assetName = $"Asset: {container.PathId}";

            try
            {
                using var fs = WaitForFileAccess(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs == null)
                {
                    Status.Write($"[Editor] Cannot read {assetName}: File is locked by editor.", StatusType.Warning);
                    return;
                }

                using var sr = new StreamReader(fs, Encoding.UTF8);
                using var exporter = new AssetImportExport(sr, leaveOpen: true);
                var tempField = workspace.GetTemplateField(container);

                if (tempField == null)
                {
                    Status.Write($"[Editor] TemplateField missing for {assetName}", StatusType.Error);
                    return;
                }

                byte[]? bytes = exporter.ImportJsonAsset(
                    tempField,
                    workspace.am.GetRefTypeManager(container.FileInstance),
                    out string? exceptionMessage);

                if (bytes != null)
                {
                    workspace.AddOrUpdateAsset(container, bytes);
                    Status.Write($"[Editor] Hot-Reloaded: {assetName}", StatusType.Success);
                    NotificationManager.Success("Asset updated in memory!", assetName, durationMs: 2500);
                }
                else if (!string.IsNullOrEmpty(exceptionMessage))
                {
                    Status.Write($"[Editor] JSON parse error in {assetName}: {exceptionMessage}", StatusType.Error);
                    NotificationManager.Warning($"Validation failed: {exceptionMessage}", assetName, durationMs: 4000);
                }
            }
            catch (Exception ex)
            {
                Status.Write($"[Editor] Hot-reload error for {assetName}: {ex.Message}", StatusType.Error);
            }
        }

        private static FileStream? WaitForFileAccess(string path, FileMode mode, FileAccess access, FileShare share)
        {
            const int maxRetries = 10;
            const int delayMs = 100;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return new FileStream(path, mode, access, share);
                }
                catch (IOException)
                {
                    Thread.Sleep(delayMs);
                }
            }
            return null;
        }

        private static void RemoveWatcher(string path)
        {
            if (Watchers.TryRemove(path, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            FileMap.TryRemove(path, out _);

            if (DebounceTokens.TryRemove(path, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        public static bool TryOpenWith(string appName, string arg)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = appName,
                    Arguments = arg,
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                var p = Process.Start(psi);
                return p != null;
            }
            catch
            {
                return false;
            }
        }
    }
}