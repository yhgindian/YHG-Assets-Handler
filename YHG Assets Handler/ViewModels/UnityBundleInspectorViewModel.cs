using AssetsHandler.Controls;
using AssetsHandler.Core;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Utils;
using YHG_Assets_Handler.Utils.Collections;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.Views;
using YHG_Assets_Handler.Views.Windows;
using YHG_Assets_Handler.Workspace.UnityBundle;

namespace YHG_Assets_Handler.ViewModels
{
    public partial class UnityBundleInspectorViewModel : BaseViewModel
    {
        public UnityBundleWorkspace Workspace { get; }
        public AssetsManager am => Workspace.am;
        private WeakReference<DarkDataGrid_01>? _gridRef;

        // Serializes underlying stream access across child inspectors of this bundle.e
        private readonly SemaphoreSlim _childLoadGate = new(1, 1);

        public string EngineVersion => Workspace.BundleInst?.file?.Header?.EngineVersion ?? "N/A";

        public string BundleCompression => Workspace.BundleInst != null
            ? Workspace.BundleInst.originalCompression.ToString()
            : "None";

        public uint FormatVersion => Workspace.BundleInst?.file?.Header?.Version ?? 0;


        [ObservableProperty]
        private ObservableRangeCollection<UnityBundleContainer> _bundleEntries = new();

        [ObservableProperty]
        private UnityBundleContainer? _selectedBundleItem;

        [ObservableProperty]
        private List<UnityBundleContainer> _selectedBundleItems = new();

        public UnityBundleInspectorViewModel(ExplorerItem item) : base(item)
        {
            Workspace = new UnityBundleWorkspace();
            Init();
        }

        private async void Init()
        {
            if (!File.Exists(item.FullPath))
            {
                Status.Write("File does not exist", StatusType.Error);
                return;
            }

            await LoadBundleAsync(item.FullPath);
        }

        private void TryLoadClassDatabase(string? version)
        {
            if (am.ClassDatabase != null) return;
            if (string.IsNullOrEmpty(version) || version == "0.0.0") return;

            try { am.LoadClassDatabaseFromPackage(version); }
            catch (Exception ex)
            {
                Status.Write($"[Bundle] Failed to load class database ({version}): {ex.Message}", StatusType.Warning);
            }
        }

        private async ValueTask LoadBundleAsync(string path)
        {
            try
            {
                List<UnityBundleContainer>? loadedEntries = null;
                List<ExplorerItem>? loadedChildren = null;

                await ProcessManager.RunAsync("File Importing", async h =>
                {
                    await Task.Run(() =>
                    {
                        h.Token.ThrowIfCancellationRequested();

                        var inst = am.LoadBundleFile(path)
                                   ?? throw new InvalidDataException("Bundle could not be loaded.");
                        Workspace.BundleInst = inst;

                        TryLoadClassDatabase(inst.file.Header.EngineVersion);

                        (loadedEntries, loadedChildren) = GenerateBundleEntries(
                            inst, (p, t) => h.Report(p, t), h.Token);
                    }, h.Token);
                });

                if (loadedEntries != null && loadedChildren != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        BundleEntries.Clear();
                        BundleEntries.AddRange(loadedEntries);

                        item.Children.Clear();
                        foreach (var child in loadedChildren)
                            item.Children.Add(child);
                    });
                }
            }
            catch (Exception ex)
            {
                Status.Write($"Failed to load bundle: {ex.Message}", StatusType.Error);
            }
        }

        private (List<UnityBundleContainer> entries, List<ExplorerItem> children) GenerateBundleEntries(
            BundleFileInstance inst, Action<double, string>? report, CancellationToken ct)
        {
            var dirInfos = inst.file.BlockAndDirInfo.DirectoryInfos;
            int fileCount = dirInfos.Count;
            var tempEntries = new List<UnityBundleContainer>(fileCount);
            var tempChildren = new List<ExplorerItem>(fileCount);

            for (int i = 0; i < fileCount; i++)
            {
                ct.ThrowIfCancellationRequested();

                var dirInfo = dirInfos[i];
                report?.Invoke(((i + 1) * 100.0) / Math.Max(1, fileCount), $"Reading ({i + 1}/{fileCount}): {dirInfo.Name}");

                var cont = new UnityBundleContainer(dirInfo, isNew: false, index: i);
                var explorerChild = CreateBundleItem(inst, dirInfo, i);
                cont.AssociatedItem = explorerChild;

                if (cont.IsSerialized)
                {
                    try
                    {
                        AssetsFileInstance? childInst = am.LoadAssetsFileFromBundle(inst, i);
                        if (childInst?.file == null)
                            throw new InvalidDataException("Serialized file is not preview-safe / could not be parsed.");

                        TryLoadClassDatabase(childInst.file.Metadata.UnityVersion);

                        var inspectorVm = new UnityAssetsInspectorViewModel(explorerChild, am, childInst, _childLoadGate);
                        explorerChild.SetCachedViewModel(inspectorVm);

                        // 1. Live modification sync
                        inspectorVm.workspace.MarkModified += (isDirty) =>
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                cont.IsModified = isDirty;

                                if (isDirty)
                                {
                                    Workspace.SetDirty();
                                    IsModified = true;
                                }
                            });
                        };

                        // 2. Child save commit hook (SetNewData replacer assign)
                        inspectorVm.OnChildFileCommitted = (fileInst) =>
                        {
                            cont.DirectoryInfo.SetNewData(fileInst.file);
                            cont.IsModified = true;
                            Workspace.SetDirty();
                            IsModified = true;

                            Status.Write($"[Bundle] Replacer linked for '{cont.Name}'. Ready for bundle pack.", StatusType.Success);
                        };
                    }
                    catch (Exception ex)
                    {
                        explorerChild.FileType = FileType.Unknown;
                        Status.Write($"[Bundle] Failed to parse internal assets for {dirInfo.Name}: {ex.Message}", StatusType.Warning);
                    }
                }

                tempChildren.Add(explorerChild);
                tempEntries.Add(cont);
            }

            report?.Invoke(100, $"Loaded {fileCount} entries.");
            return (tempEntries, tempChildren);
        }

        public override async Task<bool> OnSaveAsync(bool saveAs = false)
        {
            if (Workspace.BundleInst == null)
            {
                NotificationManager.Warning("No bundle loaded in workspace.", "Save");
                return false;
            }

            bool isDirty = IsModified || Workspace.IsDirty || BundleEntries.Any(e => e.IsModified);
            if (!isDirty && !saveAs)
            {
                NotificationManager.Info("No changes detected in bundle.", "Save");
                return true;
            }

            // 1. OptionSelectorWindow Dialog
            var parameters = new List<OptionParam>
    {
        new OptionParam("compression", "Compression Type", OptionKind.Choice,
            defaultValue: AssetBundleCompressionType.LZ4,
            description: "LZ4 recommended for games (runtime chunk streaming). LZMA gives smallest disk size.")
        {
            Choices = new object[]
            {
                AssetBundleCompressionType.LZ4,
                AssetBundleCompressionType.LZ4Fast,
                AssetBundleCompressionType.LZMA,
                AssetBundleCompressionType.None
            }
        },
        new OptionParam("blockDirAtEnd", "Pack Directory Info at File End", OptionKind.Bool,
            defaultValue: true,
            description: "Default for UnityFS bundles. Keeps raw data streaming standard.")
    };

            var optResult = OptionSelectorWindow.Ask(
                Application.Current.MainWindow,
                "Bundle Compression Settings",
                parameters,
                "Choose repack configuration for this AssetBundle:"
            );

            if (optResult == null) return false;

            var selectedCompression = (AssetBundleCompressionType)optResult["compression"]!;
            bool blockDirAtEnd = (bool)optResult["blockDirAtEnd"]!;

            var bunInst = Workspace.BundleInst;

            // 2. Child ViewModels commit
            var modifiedEntries = BundleEntries.Where(e => e.IsModified).ToList();
            foreach (var entry in modifiedEntries)
            {
                if (entry.AssociatedItem?.GetCachedViewModel<UnityAssetsInspectorViewModel>() is UnityAssetsInspectorViewModel childVm)
                {
                    if (childVm.workspace.IsDirty || childVm.IsModified)
                    {
                        await childVm.OnSaveAsync();
                    }
                }
            }

            string origBundlePath = item.FullPath;
            string destPath = origBundlePath;

            if (saveAs)
            {
                var sfd = new SaveFileDialog
                {
                    Title = "Save AssetBundle As",
                    FileName = Path.GetFileName(origBundlePath),
                    Filter = "All Files (*.*)|*.*",
                    InitialDirectory = Path.GetDirectoryName(origBundlePath)
                };

                if (sfd.ShowDialog() != true) return false;
                destPath = sfd.FileName;
            }

            string dir = Path.GetDirectoryName(destPath)!;
            string tempFilePath = Path.Combine(dir, "~" + Path.GetFileName(destPath) + ".tmp");
            string uncompressedTemp = tempFilePath + ".raw";

            bool success = await ProcessManager.RunAsync("Saving AssetBundle", async h =>
            {
                await Task.Run(() =>
                {
                    h.Token.ThrowIfCancellationRequested();

                    // ─────────────────────────────────────────────────────────────
                    // SCENARIO A: None (Uncompressed direct write)
                    // ─────────────────────────────────────────────────────────────
                    if (selectedCompression == AssetBundleCompressionType.None)
                    {
                        h.Report(20, "Writing uncompressed bundle...");
                        using (var tempFs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (var writer = new AssetsFileWriter(tempFs))
                        {
                            bunInst.file.Write(writer);
                        }
                    }
                    // ─────────────────────────────────────────────────────────────
                    // SCENARIO B: Repack with Compression (LZ4 / LZMA)
                    // ─────────────────────────────────────────────────────────────
                    else
                    {
                        // Step 1: Write raw uncompressed stream first
                        h.Report(15, "Collecting and writing modified bundle data...");
                        using (var rawFs = new FileStream(uncompressedTemp, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (var rawWriter = new AssetsFileWriter(rawFs))
                        {
                            bunInst.file.Write(rawWriter);
                        }

                        h.Token.ThrowIfCancellationRequested();

                        // Step 2: Read raw uncompressed bundle and Pack to final target
                        h.Report(40, $"Initializing {selectedCompression} compressor...");

                        var compressProgress = new BundleCompressProgressBridge(
                            (pct, msg) => h.Report(pct, msg),
                            startPct: 40.0,
                            weight: 45.0,
                            prefix: $"Compressing ({selectedCompression})"
                        );

                        using (var rawReadStream = File.OpenRead(uncompressedTemp))
                        {
                            var rawBundle = new AssetBundleFile();
                            rawBundle.Read(new AssetsFileReader(rawReadStream));

                            using (var compFs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                            using (var compWriter = new AssetsFileWriter(compFs))
                            {
                                rawBundle.Pack(compWriter, selectedCompression, blockDirAtEnd, compressProgress);
                            }

                            rawBundle.Close();
                        }

                        try { File.Delete(uncompressedTemp); } catch { }
                    }

                    // ─────────────────────────────────────────────────────────────
                    // FINAL PHASE: Replace original file and reload
                    // ─────────────────────────────────────────────────────────────
                    h.Report(88, "Replacing bundle on disk...");

                    if (!saveAs)
                    {
                        bunInst.file.Close();
                        GC.Collect();
                        GC.WaitForPendingFinalizers();

                        File.Move(tempFilePath, destPath, overwrite: true);

                        h.Report(95, "Syncing bundle metadata...");

                        var newStream = File.Open(destPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        bunInst.file = new AssetBundleFile();
                        bunInst.file.Read(new AssetsFileReader(newStream));

                        var newInfos = bunInst.file.BlockAndDirInfo.DirectoryInfos;
                        for (int i = 0; i < Math.Min(BundleEntries.Count, newInfos.Count); i++)
                        {
                            newInfos[i].Replacer = null;
                            BundleEntries[i].UpdateDirectoryInfo(newInfos[i]);
                        }
                    }
                    else
                    {
                        File.Move(tempFilePath, destPath, overwrite: true);
                    }

                    h.Report(100, "Done.");
                }, h.Token);
            });

            if (success)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var entry in BundleEntries)
                    {
                        entry.IsModified = false;
                    }
                    Workspace.MarkSaved();
                    IsModified = false;
                });

                Status.Write($"[Bundle] Saved '{Path.GetFileName(destPath)}' with {selectedCompression} compression.", StatusType.Success);
                NotificationManager.Success("Bundle saved successfully!", Path.GetFileName(destPath));
                return true;
            }

            // Cleanup temp files on cancel / fail
            if (File.Exists(tempFilePath)) try { File.Delete(tempFilePath); } catch { }
            if (File.Exists(uncompressedTemp)) try { File.Delete(uncompressedTemp); } catch { }

            return false;
        }

        public ExplorerItem CreateBundleItem(BundleFileInstance inst, AssetBundleDirectoryInfo dirInfo, int index = -1)
        {
            bool isSerialized = (dirInfo.Flags & 0x04) != 0;

            return new ExplorerItem
            {
                Name = dirInfo.Name,
                FullPath = $"{item.FullPath}/{dirInfo.Name}",
                Parent = item,
                IsFolder = false,
                FileType = isSerialized ? FileType.UnityAsset : FileType.Unknown
            };
        }

        public void RemoveSelectedEntries()
        {
            if (Workspace.BundleInst == null || SelectedBundleItems.Count == 0) return;

            var itemsToRemove = SelectedBundleItems.ToList();
            var dirInfos = Workspace.BundleInst.file.BlockAndDirInfo.DirectoryInfos;

            foreach (var target in itemsToRemove)
            {
                dirInfos.Remove(target.DirectoryInfo);
                BundleEntries.Remove(target);

                if (target.AssociatedItem != null)
                {
                    try { target.AssociatedItem.CachedViewModel?.Dispose(); } catch { }
                    target.AssociatedItem.CachedViewModel = null;
                    item.Children.Remove(target.AssociatedItem);
                }
            }

            for (int i = 0; i < BundleEntries.Count; i++)
                BundleEntries[i].Index = i;

            SelectedBundleItems.Clear();
            SelectedBundleItem = null;
            SelectedCounts = 0;

            Workspace.SetDirty();
            IsModified = true;
        }

        public void UpdateSelection(IList items)
        {
            SelectedCounts = items.Count;
            var list = items.Cast<UnityBundleContainer>().ToList();
            SelectedBundleItems = list;
            SelectedBundleItem = list.FirstOrDefault();
        }

        public void SetControls(DarkDataGrid_01 grid) => _gridRef = new(grid);

        private DarkDataGrid_01? GetGrid()
            => _gridRef != null && _gridRef.TryGetTarget(out var grid) ? grid : null;

        public override void OnClose()
        {
            // 1. Saare child ViewModels ko explicitly dispose karo aur event unhook karo
            if (BundleEntries != null)
            {
                foreach (var entry in BundleEntries)
                {
                    if (entry.AssociatedItem != null)
                    {
                        if (entry.AssociatedItem.CachedViewModel is IDisposable disposableVm)
                        {
                            try
                            {
                                disposableVm.Dispose(); // Ye child ka OnClose() trigger karega
                            }
                            catch (Exception ex)
                            {
                                Status.Write($"[Cleanup] Error disposing child VM '{entry.Name}': {ex.Message}", StatusType.Warning);
                            }
                        }

                        // Reference tod do taaki GC collect kar sake
                        entry.AssociatedItem.CachedViewModel = null;
                        entry.AssociatedItem.Parent = null;
                    }

                    entry.AssociatedItem = null;
                }

                BundleEntries.Clear();
            }

            // 2. ExplorerItem ke children collection ko clear karo
            if (item?.Children != null)
            {
                item.Children.Clear();
            }

            // 3. Selection list clear karo
            SelectedBundleItems?.Clear();
            SelectedBundleItem = null;

            // 4. Semaphore dispose karo
            try
            {
                _childLoadGate.Dispose();
            }
            catch { }

            // 5. Grid weak reference null karo
            _gridRef = null;

            // 6. Parent bundle workspace aur streams close karo
            Workspace.Dispose();

            base.OnClose();
        }

        [RelayCommand]
        private async Task ImportAsync()
        {
            if (SelectedBundleItem == null || Workspace.BundleInst == null)
            {
                NotificationManager.Warning("Select an item to replace.", "Import");
                return;
            }

            var entry = SelectedBundleItem;

            if (entry.IsModified || entry.DirectoryInfo.Replacer != null)
            {
                bool confirmed = ConfirmationDialog.Show(
                    "Overwrite Unsaved Changes?",
                    $"The entry '{entry.Name}' has unsaved changes that will be permanently overwritten by this import.\n\nDo you want to proceed and replace it anyway?"
                );

                if (!confirmed)
                    return;
            }

            var ofd = new OpenFileDialog
            {
                Title = $"Import (Replace) for {entry.Name}",
                FileName = entry.Name,
                Filter = "All Files (*.*)|*.*",
                InitialDirectory = Path.GetDirectoryName(item.FullPath)
            };

            if (ofd.ShowDialog() != true) return;

            string replacementFile = ofd.FileName;
            string newFileName = Path.GetFileName(replacementFile);
            long newSize = new FileInfo(replacementFile).Length;

            await ProcessManager.RunAsync("Importing Replacement", async h =>
            {
                await Task.Run(() =>
                {
                    h.Token.ThrowIfCancellationRequested();
                    h.Report(20, "Reading replacement file...");

                    byte[] newBytes = File.ReadAllBytes(replacementFile);

                    using var checkStream = new MemoryStream(newBytes);
                    FileType detectedType = FileTypeDetector.DetectFileType(checkStream);
                    bool isActuallyAsset = (detectedType == FileType.UnityAsset);

                    if (entry.DirectoryInfo.Replacer is IDisposable oldReplacer)
                    {
                        try { oldReplacer.Dispose(); } catch { }
                    }

                    entry.DirectoryInfo.Replacer = new ContentReplacerFromBuffer(newBytes);
                    entry.DirectoryInfo.Name = newFileName;

                    if (isActuallyAsset)
                        entry.DirectoryInfo.Flags |= 0x04;
                    else
                        entry.DirectoryInfo.Flags &= ~0x04u;

                    if (entry.AssociatedItem != null)
                    {
                        if (entry.AssociatedItem.CachedViewModel is IDisposable oldVm)
                        {
                            try { oldVm.Dispose(); } catch { }
                            entry.AssociatedItem.CachedViewModel = null;
                        }

                        var existingInst = am.Files.FirstOrDefault(f => f != null && (f.name == entry.Name || f.name == newFileName));
                        if (existingInst != null)
                        {
                            am.UnloadAssetsFile(existingInst);
                        }

                        if (isActuallyAsset)
                        {
                            h.Report(50, "Valid Unity Asset detected. Rebuilding inspector...");

                            try
                            {
                                var ms = new MemoryStream(newBytes);
                                var newFileInst = am.LoadAssetsFile(ms, newFileName, false);

                                if (newFileInst?.file != null)
                                {
                                    TryLoadClassDatabase(newFileInst.file.Metadata.UnityVersion);

                                    var newInspectorVm = new UnityAssetsInspectorViewModel(
                                        entry.AssociatedItem,
                                        am,
                                        newFileInst,
                                        _childLoadGate
                                    );

                                    newInspectorVm.workspace.MarkModified += (isDirty) =>
                                    {
                                        Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            entry.IsModified = isDirty;
                                            if (isDirty)
                                            {
                                                Workspace.SetDirty();
                                                IsModified = true;
                                            }
                                        });
                                    };

                                    newInspectorVm.OnChildFileCommitted = (fileInst) =>
                                    {
                                        entry.DirectoryInfo.Replacer = new ContentReplacerFromAssets(fileInst);
                                        entry.IsModified = true;
                                        Workspace.SetDirty();
                                        IsModified = true;
                                        Status.Write($"[Bundle] Replacer linked for '{entry.Name}'.", StatusType.Success);
                                    };

                                    entry.AssociatedItem.SetCachedViewModel(newInspectorVm);
                                    entry.AssociatedItem.FileType = FileType.UnityAsset;
                                }
                            }
                            catch (Exception ex)
                            {
                                Status.Write($"[Bundle] Failed to parse imported asset: {ex.Message}", StatusType.Warning);
                            }
                        }
                        else
                        {
                            entry.AssociatedItem.FileType = detectedType != FileType.Unknown ? detectedType : FileType.Unknown;
                        }
                    }

                    h.Report(100, "Done.");
                }, h.Token);
            });

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Container Model update (DataGrid)
                entry.Name = newFileName;
                entry.Size = newSize;
                entry.IsModified = true;

                // TreeView Item update
                if (entry.AssociatedItem != null)
                {
                    entry.AssociatedItem.Name = newFileName;
                    entry.AssociatedItem.FullPath = $"{item.FullPath}/{newFileName}";
                    entry.AssociatedItem.ResetFileTypeCache();
                }

                Workspace.SetDirty();
                IsModified = true;
            });

            Status.Write($"[Bundle] Replaced '{entry.Name}' with '{newFileName}'.", StatusType.Success);
            NotificationManager.Success($"Replaced '{entry.Name}' successfully.", "Import Complete");
        }

        [RelayCommand]
        private async Task ExportAsync()
        {
            if (Workspace.BundleInst == null)
            {
                NotificationManager.Warning("No bundle loaded.", "Export");
                return;
            }

            var targets = SelectedBundleItems?.Count > 0
                ? SelectedBundleItems.ToList()
                : (SelectedBundleItem != null ? new List<UnityBundleContainer> { SelectedBundleItem } : null);

            if (targets == null || targets.Count == 0)
            {
                NotificationManager.Warning("Select at least one item to export.", "Export");
                return;
            }

            // Case 1: Single file export -> SaveFileDialog
            if (targets.Count == 1)
            {
                var entry = targets[0];
                var sfd = new SaveFileDialog
                {
                    Title = $"Export {entry.Name}",
                    FileName = entry.Name,
                    Filter = "All Files (*.*)|*.*",
                    InitialDirectory = Path.GetDirectoryName(item.FullPath)
                };

                if (sfd.ShowDialog() != true) return;

                string destPath = sfd.FileName;

                await ProcessManager.RunAsync("Exporting Entry", async h =>
                {
                    await Task.Run(() =>
                    {
                        h.Token.ThrowIfCancellationRequested();
                        h.Report(20, $"Exporting {entry.Name}...");

                        ExportSingleEntry(entry, destPath);

                        h.Report(100, "Done.");
                    }, h.Token);
                });

                Status.Write($"[Bundle] Exported '{entry.Name}' successfully.", StatusType.Success);
                NotificationManager.Success($"Exported {entry.Name}", "Export Completed");
                return;
            }

            // Case 2: Multiple files export -> OpenFolderDialog
            var fbd = new OpenFolderDialog
            {
                Title = $"Select folder to export {targets.Count} entries",
                InitialDirectory = Path.GetDirectoryName(item.FullPath),
                Multiselect = false
            };

            if (fbd.ShowDialog() != true) return;

            string targetDir = fbd.FolderName;

            await ProcessManager.RunAsync($"Exporting {targets.Count} Entries", async h =>
            {
                await Task.Run(() =>
                {
                    int total = targets.Count;

                    for (int i = 0; i < total; i++)
                    {
                        h.Token.ThrowIfCancellationRequested();

                        var entry = targets[i];
                        double pct = ((i + 1) * 100.0) / total;
                        h.Report(pct, $"Extracting ({i + 1}/{total}): {entry.Name}");

                        string destPath = Path.Combine(targetDir, entry.Name);

                        try
                        {
                            ExportSingleEntry(entry, destPath);
                        }
                        catch (Exception ex)
                        {
                            Status.Write($"[Bundle] Failed to export '{entry.Name}': {ex.Message}", StatusType.Error);
                        }
                    }

                    h.Report(100, "Export completed.");
                }, h.Token);
            });

            Status.Write($"[Bundle] Successfully exported {targets.Count} items to '{targetDir}'.", StatusType.Success);
            NotificationManager.Success($"Exported {targets.Count} items successfully.", "Batch Export Complete");
        }

        private void ExportSingleEntry(UnityBundleContainer entry, string destFile)
        {
            using var outFs = File.Create(destFile);
            using var writer = new AssetsFileWriter(outFs);

            var dirInfo = entry.DirectoryInfo;

            // Case 1: Active inspector VM exists in memory with unsaved/latest mutations.
            if (entry.AssociatedItem?.CachedViewModel is UnityAssetsInspectorViewModel inspectorVm &&
                inspectorVm.workspace.LoadedFiles.Count > 0 &&
                (entry.IsModified || inspectorVm.workspace.IsDirty))
            {
                var mainInst = inspectorVm.workspace.LoadedFiles[0];

                if (mainInst?.file != null)
                {
                    var reader = mainInst.LockReader;
                    lock (reader)
                    {
                        mainInst.file.Write(writer, -1);
                    }
                    return;
                }
            }

            // Case 2: Process queued replacers from staged imports or uncommitted overrides
            if (dirInfo.Replacer != null)
            {
                if (dirInfo.Replacer.HasPreview())
                {
                    using var previewStream = dirInfo.Replacer.GetPreviewStream();
                    previewStream.CopyTo(outFs);
                }
                else
                {
                    dirInfo.Replacer.Write(writer, finalWrite: false);
                }
                return;
            }

            // Case 3: Read raw, unmodified stream directly from the underlying bundle container
            var bundleReader = Workspace.BundleInst!.file.Reader;

            lock (bundleReader)
            {
                bundleReader.Position = dirInfo.Offset;

                byte[] buffer = new byte[81920]; // 80 KB transfer buffer
                long remaining = dirInfo.DecompressedSize;

                while (remaining > 0)
                {
                    int toRead = (int)Math.Min(buffer.Length, remaining);
                    int read = bundleReader.Read(buffer, 0, toRead);
                    if (read <= 0) break;

                    outFs.Write(buffer, 0, read);
                    remaining -= read;
                }
            }
        }

        [RelayCommand]
        private async Task AddAsync()
        {
            if (Workspace.BundleInst == null)
            {
                NotificationManager.Warning("Please load a bundle first.", "Add Entry");
                return;
            }

            var ofd = new OpenFileDialog
            {
                Title = "Select File to Add to Bundle",
                Filter = "All Files (*.*)|*.*",
                Multiselect = true
            };

            if (ofd.ShowDialog() != true) return;

            var selectedFiles = ofd.FileNames;
            if (selectedFiles.Length == 0) return;

            await ProcessManager.RunAsync("Adding Entries to Bundle", async h =>
            {
                await Task.Run(() =>
                {
                    int total = selectedFiles.Length;

                    for (int i = 0; i < total; i++)
                    {
                        h.Token.ThrowIfCancellationRequested();
                        string filePath = selectedFiles[i];
                        string fileName = Path.GetFileName(filePath);

                        h.Report(((i + 1) * 100.0) / total, $"Adding ({i + 1}/{total}): {fileName}");

                        // 1. Check duplicate name in current bundle entries
                        bool exists = BundleEntries.Any(e => string.Equals(e.Name, fileName, StringComparison.OrdinalIgnoreCase));
                        if (exists)
                        {
                            Status.Write($"[Bundle Add] Skipped '{fileName}': Entry with this name already exists.", StatusType.Warning);
                            continue;
                        }

                        byte[] fileBytes = File.ReadAllBytes(filePath);
                        long fileSize = fileBytes.Length;

                        using var checkStream = new MemoryStream(fileBytes);
                        FileType detectedType = FileTypeDetector.DetectFileType(checkStream);
                        bool isSerialized = (detectedType == FileType.UnityAsset);

                        var newDirInfo = new AssetBundleDirectoryInfo
                        {
                            Name = fileName,
                            Offset = 0,
                            DecompressedSize = fileSize,
                            Flags = isSerialized ? 0x04u : 0x00u,
                            Replacer = new ContentReplacerFromBuffer(fileBytes)
                        };

                        Workspace.BundleInst.file.BlockAndDirInfo.DirectoryInfos.Add(newDirInfo);

                        // 4. Associated ExplorerItem for TreeView
                        var childItem = new ExplorerItem
                        {
                            Name = fileName,
                            FullPath = $"{item.FullPath}/{fileName}",
                            IsFolder = false,
                            Parent = item,
                            FileType = isSerialized ? FileType.UnityAsset : detectedType
                        };

                        if (isSerialized)
                        {
                            try
                            {
                                var ms = new MemoryStream(fileBytes);
                                var newFileInst = am.LoadAssetsFile(ms, fileName, false);

                                if (newFileInst?.file != null)
                                {
                                    TryLoadClassDatabase(newFileInst.file.Metadata.UnityVersion);

                                    var newInspectorVm = new UnityAssetsInspectorViewModel(
                                        childItem,
                                        am,
                                        newFileInst,
                                        _childLoadGate
                                    );

                                    newInspectorVm.workspace.MarkModified += (isDirty) =>
                                    {
                                        Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            if (isDirty)
                                            {
                                                Workspace.SetDirty();
                                                IsModified = true;
                                            }
                                        });
                                    };

                                    newInspectorVm.OnChildFileCommitted = (fileInst) =>
                                    {
                                        newDirInfo.Replacer = new ContentReplacerFromAssets(fileInst);
                                        Workspace.SetDirty();
                                        IsModified = true;
                                        Status.Write($"[Bundle] Replacer linked for newly added '{fileName}'.", StatusType.Success);
                                    };

                                    childItem.SetCachedViewModel(newInspectorVm);
                                }
                            }
                            catch (Exception ex)
                            {
                                Status.Write($"[Bundle Add] Failed to parse inspector for '{fileName}': {ex.Message}", StatusType.Warning);
                            }
                        }

                        var container = new UnityBundleContainer(newDirInfo, isNew: true, index: BundleEntries.Count)
                        {
                            AssociatedItem = childItem
                        };

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            BundleEntries.Add(container);
                            item.Children.Add(childItem);
                        });
                    }

                    h.Report(100, "Done.");
                }, h.Token);
            });

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Workspace.SetDirty();
                IsModified = true;
            });

            Status.Write($"[Bundle] Added {selectedFiles.Length} file(s) to bundle. Save bundle to commit.", StatusType.Success);
            NotificationManager.Success($"Added file(s) successfully.", "Bundle Add Complete");
        }

        [RelayCommand]
        private async Task DeleteAsync()
        {
            if (Workspace.BundleInst == null)
            {
                NotificationManager.Warning("No bundle loaded.", "Delete Entry");
                return;
            }

            var targets = SelectedBundleItems?.Count > 0
                ? SelectedBundleItems.ToList()
                : (SelectedBundleItem != null ? new List<UnityBundleContainer> { SelectedBundleItem } : null);

            if (targets == null || targets.Count == 0)
            {
                NotificationManager.Warning("Select at least one entry to delete.", "Delete Entry");
                return;
            }

            string confirmMsg = targets.Count == 1
                ? $"Delete '{targets[0].Name}' from the bundle?"
                : $"Delete {targets.Count} entries from the bundle?";

            bool confirmed = ConfirmationDialog.Show("Confirm Delete", confirmMsg);
            if (!confirmed) return;

            await ProcessManager.RunAsync("Deleting Entries", async h =>
            {
                await Task.Run(() =>
                {
                    int total = targets.Count;

                    for (int i = 0; i < total; i++)
                    {
                        h.Token.ThrowIfCancellationRequested();
                        var entry = targets[i];
                        h.Report(((i + 1) * 100.0) / total, $"Removing ({i + 1}/{total}): {entry.Name}");

                        // 1. Replacer cleanup
                        if (entry.DirectoryInfo.Replacer is IDisposable replacer)
                        {
                            try { replacer.Dispose(); } catch { }
                            entry.DirectoryInfo.Replacer = null;
                        }

                        // 2. Memory & Cache purge
                        if (entry.AssociatedItem != null)
                        {
                            if (entry.AssociatedItem.CachedViewModel is IDisposable vm)
                            {
                                try { vm.Dispose(); } catch { }
                                entry.AssociatedItem.CachedViewModel = null;
                            }

                            var stale = am.Files.FirstOrDefault(f => f != null && (f.name == entry.Name || f.name == entry.DirectoryInfo.Name));
                            if (stale != null)
                            {
                                try { am.UnloadAssetsFile(stale); } catch { }
                            }
                        }

                        // 3. Direct low-level directory remove
                        Workspace.BundleInst.file.BlockAndDirInfo.DirectoryInfos.Remove(entry.DirectoryInfo);
                    }

                    h.Report(100, "Done.");
                }, h.Token);
            });

            // 4. UI sync
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var entry in targets)
                {
                    BundleEntries.Remove(entry);

                    if (entry.AssociatedItem != null)
                    {
                        item.Children.Remove(entry.AssociatedItem);
                    }
                }

                for (int i = 0; i < BundleEntries.Count; i++)
                {
                    BundleEntries[i].Index = i;
                }

                SelectedBundleItems?.Clear();
                SelectedBundleItem = null;

                Workspace.SetDirty();
                IsModified = true;
            });

            Status.Write($"[Bundle] Removed {targets.Count} entry/entries. Save bundle to apply.", StatusType.Success);
            NotificationManager.Success($"Removed {targets.Count} entry/entries.", "Delete Complete");
        }
    }
}