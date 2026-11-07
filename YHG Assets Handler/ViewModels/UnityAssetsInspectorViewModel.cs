using AssetsHandler.Controls;
using AssetsHandler.Core;
using AssetsHandler.Core.Filtering;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using YHG_Assets_Handler.Editor.Unity;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Utils.Collections;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.ViewModels.Unity;
using YHG_Assets_Handler.Views;
using YHG_Assets_Handler.Views.Windows;
using YHG_Assets_Handler.Views.Windows.Unity;
using YHG_Assets_Handler.Workspace.UnityAssets;
using YHG_Assets_Handler.Workspace.UnityAssets.Plugins;

namespace YHG_Assets_Handler.ViewModels
{
    public partial class UnityAssetsInspectorViewModel : BaseViewModel
    {
        public UnityAssetWorkspace workspace;
        private AssetsManager am => workspace.am;
        private WeakReference<DarkDataGrid_01>? _gridRef;

        [ObservableProperty]
        private ObservableRangeCollection<UnityAssetObjectModel> assetObjects = new();

        private Dictionary<AssetContainer, UnityAssetObjectModel> assetContainerMap = new();

        [ObservableProperty]
        private List<UnityAssetObjectModel> selectedItems = new();

        [ObservableProperty]
        private UnityAssetObjectModel? activeAssetObject;

        private readonly SemaphoreSlim _loadGate = new(1, 1);
        private readonly HashSet<AssetsFileInstance> _modeledFiles = new();

        private readonly UnityContainerTool _containerTool = new();
        private readonly HashSet<string> _rsrcManLoadedDirs = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _containerLock = new();

        // Lazy initialization: Child bundle entry is loaded only when selected
        private AssetsFileInstance? _pendingInst;
        private int _pendingStarted;
        public Action<AssetsFileInstance>? OnChildFileCommitted { get; set; }

        public InspectorPanel inspectorMode = InspectorPanel.Info;

        public UnityAssetsInspectorViewModel(ExplorerItem item, bool AutoInit = true) : base(item)
        {
            workspace = new UnityAssetWorkspace();
            InitWorkspaceEvents();
            if (AutoInit)
                Init();
        }

        // Inherits the parent bundle's AssetsManager with shared class database; ownership retained by parent
        public UnityAssetsInspectorViewModel(ExplorerItem item, AssetsManager sharedAm,
            AssetsFileInstance inst, SemaphoreSlim? sharedGate = null) : base(item)
        {
            workspace = new UnityAssetWorkspace(sharedAm, fromBundle: true, ownsManager: false);
            if (sharedGate != null) _loadGate = sharedGate;

            _pendingInst = inst;
            InitWorkspaceEvents();
        }


        /// <summary>Idempotent lazy-load entry point for bundle children; no-op on standard view models.</summary>
        public void EnsureLoaded()
        {
            var inst = _pendingInst;
            if (inst == null || Interlocked.Exchange(ref _pendingStarted, 1) == 1) return;
            _ = LoadPreloadedAsync(inst);
        }

        private async Task LoadPreloadedAsync(AssetsFileInstance inst)
        {
            await _loadGate.WaitAsync();
            try
            {
                List<UnityAssetObjectModel>? models = null;

                await ProcessManager.RunAsync($"Reading {item.Name}", async h =>
                {
                    await Task.Run(() =>
                    {
                        h.Token.ThrowIfCancellationRequested();
                        h.SetIndeterminate($"Indexing assets: {item.Name}");
                        workspace.LoadAssetsFile(inst, false);
                        models = BuildModelsForFile(inst, msg => h.SetIndeterminate(msg));
                    }, h.Token);
                });

                if (models != null)
                    await CommitModels(models);
            }
            catch (Exception ex)
            {
                Status.Write($"[Bundle Child] Failed loading assets for {item.Name}: {ex.Message}", StatusType.Error);
            }
            finally
            {
                _loadGate.Release();
            }
        }

        private void InitWorkspaceEvents()
        {
            workspace.FileLoaded += OnWorkspaceFileLoaded;
            workspace.AssetUpdated += OnWorkspaceAssetUpdated;
            workspace.MarkModified += OnWorkspaceMarkModified;
            workspace.AssetRemoved += OnWorkspaceAssetRemoved;
            workspace.CheckAssetExistRequested += OnWorkspaceCheckAssetExist;
            workspace.GoToAssetRequested += OnWorkspaceGoToAsset;
        }

        public void UpdateSelection(IList items)
        {
            var list = items.Cast<UnityAssetObjectModel>().ToList();
            SelectedItems = list;
            SelectedCounts = list.Count;
            ActiveAssetObject = list.LastOrDefault();
        }

        public void UpdateInspectorPanel(int idx)
        {
            inspectorMode = (InspectorPanel)idx;
        }

        public void SetControls(DarkDataGrid_01 grid)
        {
            _gridRef = new(grid);
        }

        private DarkDataGrid_01? GetGrid()
        {
            if (_gridRef != null && _gridRef.TryGetTarget(out var grid))
            {
                return grid;
            }
            return null;
        }

        private async void Init()
        {
            var itemPath = item.FullPath;
            if (!File.Exists(itemPath))
            {
                Status.Write("File does not exist", StatusType.Error);
                return;
            }

            await LoadFilesAsync(new[] { itemPath }, "Importing Assets File");
        }

        #region Loading (single entry point)

        public async Task<int> LoadFilesAsync(IReadOnlyList<string> paths, string title)
        {
            if (paths == null || paths.Count == 0) return 0;

            await _loadGate.WaitAsync();
            try
            {
                var allModels = new List<UnityAssetObjectModel>();
                int loaded = 0, skipped = 0;

                await ProcessManager.RunAsync(title, async h =>
                {
                    await Task.Run(() =>
                    {
                        int total = paths.Count;

                        for (int i = 0; i < total; i++)
                        {
                            h.Token.ThrowIfCancellationRequested();

                            string path = paths[i];
                            h.Report(i * 100.0 / total, $"Importing ({i + 1}/{total}): {Path.GetFileName(path)}");

                            AssetsFileInstance? inst = null;
                            try
                            {
                                inst = workspace.LoadAssetsFile(path, false,
                                    msg => h.SetIndeterminate(msg), raiseFileLoaded: false);
                            }
                            catch (Exception ex)
                            {
                                Status.Write($"[Load] {Path.GetFileName(path)}: {ex.Message}", StatusType.Error);
                            }

                            if (inst == null) { skipped++; continue; }

                            try
                            {
                                var models = BuildModelsForFile(inst, msg => h.SetIndeterminate(msg));
                                if (models == null) { skipped++; continue; } // already modeled

                                allModels.AddRange(models);
                                loaded++;
                            }
                            catch (Exception ex)
                            {
                                skipped++;
                                Status.Write($"[Load] Failed to read assets of {Path.GetFileName(path)}: {ex.Message}", StatusType.Error);
                            }
                        }

                        h.Report(100, $"Loaded {loaded} file(s), {allModels.Count} assets.");
                    }, h.Token);
                });

                await CommitModels(allModels);

                if (skipped > 0)
                    Status.Write($"[Load] {skipped} file(s) skipped (invalid, different Unity version, or already loaded).", StatusType.Warning);

                return loaded;
            }
            finally
            {
                _loadGate.Release();
            }
        }

        private List<UnityAssetObjectModel>? BuildModelsForFile(AssetsFileInstance inst, Action<string>? status = null)
        {
            if (inst?.file == null) return null;

            lock (_modeledFiles)
            {
                if (!_modeledFiles.Add(inst)) return null;
            }

            try
            {
                workspace.GenerateAssetsFileLookup();
                var containers = workspace.GetAssetContainersFrom(inst);

                status?.Invoke($"Reading containers: {Path.GetFileName(inst.path)}");
                SetupContainers(inst, containers);

                status?.Invoke($"Reading assets: {Path.GetFileName(inst.path)}");
                return BuildAssetObjects(inst, containers);
            }
            catch
            {
                lock (_modeledFiles) _modeledFiles.Remove(inst);
                throw;
            }
        }

        private Task CommitModels(List<UnityAssetObjectModel> models)
        {
            if (models.Count == 0) return Task.CompletedTask;

            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var m in models)
                    if (m.assetContainer != null)
                        assetContainerMap[m.assetContainer] = m;

                AssetObjects.AddRange(models);
            }, DispatcherPriority.Background).Task;
        }

        // Raised when workspace.LoadAssetsFile(...) is called directly with raiseFileLoaded: true.
        // Bypassed by LoadFilesAsync (raiseFileLoaded: false) to prevent duplicate registrations.
        private void OnWorkspaceFileLoaded(AssetsFileInstance inst)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var models = BuildModelsForFile(inst);
                    if (models != null)
                        await CommitModels(models);
                }
                catch (Exception ex)
                {
                    Status.Write($"[Load] Failed to read assets of {Path.GetFileName(inst?.path)}: {ex.Message}", StatusType.Error);
                }
            });
        }

        private void SetupContainers(AssetsFileInstance inst, List<AssetContainer> containers)
        {
            lock (_containerLock)
            {
                if (UnityContainerTool.TryGetBundleContainerBaseField(workspace, inst, out var actualFile, out var bf))
                {
                    _containerTool.FromAssetBundle(am, actualFile, bf);   // preload skip
                    foreach (var c in containers)
                    {
                        if (c.TypeId == (int)AssetClassID.AssetBundle)
                        {
                            c.BaseValueField = null;
                            break;
                        }
                    }
                }
                else
                {
                    string dirKey = PathUtils.GetAssetsFileDirectory(inst) ?? string.Empty;

                    if (_rsrcManLoadedDirs.Add(dirKey) &&
                        UnityContainerTool.TryGetRsrcManContainerBaseField(workspace, inst, out actualFile, out bf))
                    {
                        _containerTool.FromResourceManager(am, actualFile, bf);
                    }
                }

                if (_containerTool.AssetMap.Count == 0) return;

                foreach (var cont in containers)
                {
                    string? path = _containerTool.GetContainerPath(cont.AssetPPtr);
                    if (path != null)
                        cont.Container = path;
                }
            }
        }

        private List<UnityAssetObjectModel> BuildAssetObjects(AssetsFileInstance inst, List<AssetContainer> containers)
        {
            int fileIndex = workspace.LoadedFiles.IndexOf(inst);
            var models = new List<UnityAssetObjectModel>(containers.Count);

            foreach (var cont in containers)
                models.Add(MakeUnityAssetObject(cont, isNew: false, addInList: false, fileIndex: fileIndex));

            return models;
        }

        private UnityAssetObjectModel MakeUnityAssetObject(AssetContainer cont, bool isNew = false, bool addInList = true, int fileIndex = -1)
        {
            AssetNameUtils.GetDisplayNameQuick(workspace, cont, out string name, out string type);

            var objectModel = new UnityAssetObjectModel
            {
                Name = name,
                Container = cont.Container,
                Type = type,
                FileID = fileIndex >= 0 ? fileIndex : workspace.LoadedFiles.IndexOf(cont.FileInstance),
                PathID = cont.PathId,
                Size = cont.FinalSize,
                Modified = isNew ? "*" : string.Empty,
                TypeClass = (AssetClassID)cont.TypeId,
                assetContainer = cont
            };

            if (addInList)
            {
                AssetObjects.Add(objectModel);
                assetContainerMap[cont] = objectModel;
            }

            return objectModel;
        }

        #endregion

        private void OnWorkspaceAssetUpdated(AssetContainer cont, bool isNew)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (isNew)
                {
                    MakeUnityAssetObject(cont, isNew: isNew);
                }
                else if (assetContainerMap.TryGetValue(cont, out var model))
                {
                    model.RefreshFromContainer(workspace);
                }
            });
        }

        private void OnWorkspaceAssetRemoved(AssetContainer cont)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (assetContainerMap.Remove(cont, out var model))
                {
                    AssetObjects.Remove(model);
                }
            });
        }

        private bool OnCheckAssetExist(AssetPPtr pptr)
        {
            if (pptr == null) return false;
            return AssetObjects.Any(x => x.FileID == pptr.FileId && x.PathID == pptr.PathId);
        }

        private void OnWorkspaceMarkModified(bool isModified)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsModified = isModified;
            });
        }

        private bool OnWorkspaceCheckAssetExist(AssetPPtr pptr)
        {
            return Application.Current.Dispatcher.Invoke(() =>
            {
                return OnCheckAssetExist(pptr);
            });
        }

        private void OnWorkspaceGoToAsset(AssetPPtr pptr)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (pptr == null) return;

                var target = AssetObjects.FirstOrDefault(x => x.FileID == pptr.FileId && x.PathID == pptr.PathId);
                if (target != null)
                {
                    ActiveAssetObject = target;

                    var grid = GetGrid();
                    if (grid != null)
                    {
                        grid.SelectedItem = target;
                        grid.ScrollIntoView(target);
                        grid.Focus();
                    }
                }
            });
        }

        [RelayCommand]
        private void Plugins(FrameworkElement? targetButton)
        {
            if (SelectedItems is not { Count: > 0 } items)
            {
                NotificationManager.Warning(
                    "Please select at least one asset.",
                    "Plugins",
                    key: "plugins-no-selection");
                return;
            }

            if (targetButton != null)
            {
                PluginMenuCache.Show(targetButton, workspace, items);
            }
        }

        [RelayCommand]
        private void Utils(FrameworkElement? targetButton)
        {
            if (SelectedItems is not { Count: > 0 } items)
            {
                NotificationManager.Warning(
                    "Please select at least one asset.",
                    "Utils",
                    key: "Utils-no-selection");
                return;
            }

            if (targetButton != null && targetButton.ContextMenu != null)
            {
                targetButton.ContextMenu.PlacementTarget = targetButton;
                targetButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                targetButton.ContextMenu.IsOpen = true;
            }
        }

        [RelayCommand]
        private void OpenGoToDialog()
        {
            var picker = new UnityPPtrPickerView(workspace, defaultPathId: 1)
            {
                Owner = Application.Current.MainWindow
            };

            picker.ViewModel.OnApply = (file, asset) =>
            {
                var targetPtr = new AssetPPtr(file.FileId, asset.PathId);

                if (OnCheckAssetExist(targetPtr))
                {
                    OnWorkspaceGoToAsset(targetPtr);
                    Status.Write($"Navigated to: FileID {targetPtr.FileId}, PathID {targetPtr.PathId}", StatusType.Info);
                }
                else
                {
                    Status.Write($"Asset not found: FileID {targetPtr.FileId}, PathID {targetPtr.PathId}", StatusType.Warning);
                }
            };

            picker.ShowDialog();
        }

        #region Export & Import Raw
        [RelayCommand]
        private async Task ExportRaw()
        {
            if (SelectedItems == null || SelectedItems.Count == 0)
            {
                NotificationManager.Warning("Please select at least one asset to export.", "Export Raw",
                    key: "export-raw-empty");
                return;
            }

            // Case 1: Single
            if (SelectedItems.Count == 1)
            {
                var asset = SelectedItems[0];
                var sfd = new SaveFileDialog
                {
                    Title = "Export Raw Asset",
                    FileName = $"{AssetNameUtils.getSaveFileName(asset)}.dat",
                    Filter = "Raw Binary (*.dat)|*.dat|All Files (*.*)|*.*"
                };

                if (sfd.ShowDialog() != true) return;
                string outputPath = sfd.FileName;

                bool ok = await ProcessManager.RunAsync("Exporting Raw Asset", async h =>
                {
                    await Task.Run(() =>
                    {
                        h.Token.ThrowIfCancellationRequested();
                        h.Report(0, $"Exporting {asset.Name}...");
                        ExportSingleRawAsset(asset, outputPath);
                        h.Report(100, "Export completed.");
                    }, h.Token);
                });

                if (ok)
                    NotificationManager.Success(asset.Name, "Export completed",
                        onClick: () => RevealInExplorer(outputPath));
                return;
            }

            // Case 2: Batch
            var ofd = new OpenFolderDialog { Title = "Select Destination Folder for Batch Raw Export" };
            if (ofd.ShowDialog() != true) return;

            string targetFolder = ofd.FolderName;
            var itemsToExport = SelectedItems.ToList();
            int total = itemsToExport.Count;
            int successCount = 0, failCount = 0;

            bool completed = await ProcessManager.RunAsync("Batch Exporting Raw Assets", async h =>
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        h.Token.ThrowIfCancellationRequested();

                        var asset = itemsToExport[i];
                        h.Report((i + 1) * 100.0 / total, $"Exporting ({i + 1}/{total}): {asset.Name}");

                        try
                        {
                            string filePath = Path.Combine(targetFolder, $"{AssetNameUtils.getSaveFileName(asset)}.dat");
                            ExportSingleRawAsset(asset, filePath);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            Status.Write($"Export failed for {asset.PathID}: {ex.Message}", StatusType.Error);
                        }
                    }
                    h.Report(100, $"Finished: {successCount}/{total} exported.");
                }, h.Token);
            });

            if (!completed)
                NotificationManager.Warning($"Stopped after {successCount}/{total} assets.",
                    "Batch export cancelled", key: "export-raw-batch");
            else if (failCount == 0)
                NotificationManager.Success($"Exported {successCount}/{total} assets", "Batch export completed",
                    onClick: () => OpenFolder(targetFolder), key: "export-raw-batch");
            else
                NotificationManager.Warning($"{successCount} exported, {failCount} failed. Check the console.",
                    "Batch export finished with errors", durationMs: 0,
                    onClick: () => OpenFolder(targetFolder), key: "export-raw-batch");
        }

        private void ExportSingleRawAsset(UnityAssetObjectModel assetModel, string outputPath)
        {
            var cont = assetModel.assetContainer;
            if (cont == null || cont.FileInstance == null)
                throw new InvalidOperationException("Asset container is invalid or not loaded.");

            var reader = cont.FileInstance.LockReader;
            lock (reader)
            {
                using var outStream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var exporter = new AssetImportExport(outStream);
                exporter.DumpRawAsset(cont.FileReader, cont.AbsoluteByteStart, cont.FinalSize);
            }
        }

        [RelayCommand]
        private async Task ImportRaw()
        {
            if (SelectedItems == null || SelectedItems.Count == 0)
            {
                NotificationManager.Warning("Please select at least one asset to import.", "Import Raw",
                    key: "import-raw-empty");
                return;
            }

            // Case 1: Single
            if (SelectedItems.Count == 1)
            {
                var asset = SelectedItems[0];
                if (asset.assetContainer == null)
                {
                    NotificationManager.Error($"{asset.Name} is not loaded.", "Import Raw");
                    return;
                }

                var ofd = new OpenFileDialog
                {
                    Title = "Select Raw Binary File to Import",
                    Filter = "Raw Binary (*.dat)|*.dat|All Files (*.*)|*.*",
                    Multiselect = false
                };
                if (ofd.ShowDialog() != true) return;

                string filePath = ofd.FileName;
                var container = asset.assetContainer;

                bool ok = await ProcessManager.RunAsync("Importing Raw Asset", async h =>
                {
                    await Task.Run(() =>
                    {
                        h.Token.ThrowIfCancellationRequested();
                        h.Report(0, $"Importing {asset.Name}...");
                        ImportSingleRawAsset(container, filePath);
                        h.Report(100, "Raw asset imported successfully.");
                    }, h.Token);
                });

                if (ok)
                    NotificationManager.Success(asset.Name, "Import completed");
                return;
            }

            // Case 2: Batch
            var dirDlg = new OpenFolderDialog { Title = "Select import directory" };
            if (dirDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dirDlg.FolderName))
                return;

            string directory = dirDlg.FolderName;
            var selectedContainers = SelectedItems
                .Select(x => x.assetContainer)
                .OfType<AssetContainer>()
                .ToList();

            var extensions = new List<string> { "dat" };

            var vm = new BatchImportViewModel(workspace, selectedContainers, directory, extensions);
            var view = new BatchImportView(vm) { Owner = Application.Current.MainWindow };

            if (view.ShowDialog() != true)
                return;

            if (vm.ConfirmedImports.Count == 0)
            {
                NotificationManager.Info("No matching .dat files found for the selected assets.",
                    "Import Raw", key: "import-raw-none");
                return;
            }

            var importTargets = vm.ConfirmedImports.ToList();
            int total = importTargets.Count;
            int imported = 0, failed = 0;

            bool completed = await ProcessManager.RunAsync("Batch Importing Raw Assets", async h =>
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        h.Token.ThrowIfCancellationRequested();

                        var batchInfo = importTargets[i];
                        h.Report((i + 1) * 100.0 / total, $"Importing ({i + 1}/{total}): {batchInfo.AssetName}");

                        try
                        {
                            ImportSingleRawAsset(batchInfo.Container, batchInfo.ImportFilePath);
                            imported++;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            Status.Write($"Import failed for {batchInfo.AssetName}: {ex.Message}", StatusType.Error);
                        }
                    }
                    h.Report(100, $"Finished: {imported}/{total} assets imported.");
                }, h.Token);
            });

            if (!completed)
                NotificationManager.Warning($"Stopped after {imported}/{total} assets.",
                    "Batch import cancelled", key: "import-raw-batch");
            else if (failed == 0)
                NotificationManager.Success($"Imported {imported}/{total} assets", "Batch import completed",
                    key: "import-raw-batch");
            else
                NotificationManager.Warning($"{imported} imported, {failed} failed. Check the console.",
                    "Batch import finished with errors", durationMs: 0, key: "import-raw-batch");
        }

        private void ImportSingleRawAsset(AssetContainer container, string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            workspace.AddOrUpdateAsset(container, bytes);
        }

        private static void RevealInExplorer(string path)
            => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

        private static void OpenFolder(string folder)
            => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        #endregion

        #region Export & Import Json
        [RelayCommand]
        private async Task ExportDump()
        {
            if (SelectedItems == null || SelectedItems.Count == 0)
            {
                NotificationManager.Warning("Please select at least one asset to export dump.", "Export Dump",
                    key: "export-dump-empty");
                return;
            }

            // Case 1: Single
            if (SelectedItems.Count == 1)
            {
                var asset = SelectedItems[0];
                var sfd = new SaveFileDialog
                {
                    Title = "Export Asset Dump",
                    FileName = $"{AssetNameUtils.getSaveFileName(asset)}.json",
                    Filter = "JSON Dump (*.json)|*.json|Text Dump (*.txt)|*.txt|All Files (*.*)|*.*"
                };

                if (sfd.ShowDialog() != true) return;
                string outputPath = sfd.FileName;

                bool ok = await ProcessManager.RunAsync("Exporting Asset Dump", async h =>
                {
                    await Task.Run(() =>
                    {
                        h.Token.ThrowIfCancellationRequested();
                        h.Report(0, $"Dumping {asset.Name}...");
                        ExportSingleAssetDump(asset, outputPath);
                        h.Report(100, "Dump exported successfully.");
                    }, h.Token);
                });

                if (ok)
                    NotificationManager.Success(asset.Name, "Dump exported",
                        onClick: () => RevealInExplorer(outputPath));
                return;
            }

            // Case 2: Batch
            var ofd = new OpenFolderDialog { Title = "Select Destination Folder for Batch Dump Export" };
            if (ofd.ShowDialog() != true) return;

            string targetFolder = ofd.FolderName;
            var itemsToExport = SelectedItems.ToList();
            int total = itemsToExport.Count;
            int successCount = 0, failCount = 0;

            bool completed = await ProcessManager.RunAsync("Batch Exporting Asset Dumps", async h =>
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        h.Token.ThrowIfCancellationRequested();

                        var asset = itemsToExport[i];
                        h.Report((i + 1) * 100.0 / total, $"Dumping ({i + 1}/{total}): {asset.Name}");

                        try
                        {
                            string filePath = Path.Combine(targetFolder, $"{AssetNameUtils.getSaveFileName(asset)}.json");
                            ExportSingleAssetDump(asset, filePath);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            Status.Write($"Dump export failed for {asset.Name} ({asset.PathID}): {ex.Message}", StatusType.Error);
                        }
                    }
                    h.Report(100, $"Finished: {successCount}/{total} dumps exported.");
                }, h.Token);
            });

            if (!completed)
                NotificationManager.Warning($"Stopped after {successCount}/{total} dumps.",
                    "Batch dump export cancelled", key: "export-dump-batch");
            else if (failCount == 0)
                NotificationManager.Success($"Exported {successCount}/{total} dumps", "Batch dump export completed",
                    onClick: () => OpenFolder(targetFolder), key: "export-dump-batch");
            else
                NotificationManager.Warning($"{successCount} exported, {failCount} failed. Check the console.",
                    "Batch dump export finished with errors", durationMs: 0,
                    onClick: () => OpenFolder(targetFolder), key: "export-dump-batch");
        }

        private void ExportSingleAssetDump(UnityAssetObjectModel assetModel, string outputPath)
        {
            var cont = assetModel.assetContainer;
            if (cont == null || cont.FileInstance == null)
            {
                Status.Write($"[Export] Asset container is invalid or not loaded for {assetModel.Name} ({assetModel.PathID}).", StatusType.Error);
                return;
            }

            var reader = cont.FileInstance.LockReader;
            lock (reader)
            {
                if (!cont.HasValueField)
                    cont = workspace.GetAssetContainer(cont.FileInstance, 0, cont.PathId);

                if (cont?.BaseValueField is not { } baseField)
                {
                    Status.Write($"[Export] Could not deserialize type fields for asset {assetModel.Name} ({assetModel.PathID}).", StatusType.Error);
                    return;
                }

                try
                {
                    using var sw = new StreamWriter(outputPath, false, Encoding.UTF8, 8192);
                    var exporter = new AssetImportExport(sw);
                    exporter.DumpJsonAsset(baseField);

                    Status.Write($"[Export] Dump generated successfully for {assetModel.Name}.", StatusType.Success);
                }
                catch (Exception ex)
                {
                    Status.Write($"[Export] Failed to write dump file for {assetModel.Name}: {ex.Message}", StatusType.Error);
                }
            }
        }

        [RelayCommand]
        private async Task ImportDump()
        {
            if (SelectedItems == null || SelectedItems.Count == 0)
            {
                NotificationManager.Warning("Please select at least one asset to import JSON dump.", "Import Dump",
                    key: "import-dump-empty");
                return;
            }

            // Case 1: Single
            if (SelectedItems.Count == 1)
            {
                var asset = SelectedItems[0];
                var container = asset.assetContainer;
                if (container == null)
                {
                    NotificationManager.Error($"{asset.Name} is not loaded.", "Import Dump");
                    return;
                }

                var ofd = new OpenFileDialog
                {
                    Title = "Select JSON Dump File to Import",
                    Filter = "JSON Dump (*.json)|*.json|Text Dump (*.txt)|*.txt|All Files (*.*)|*.*",
                    Multiselect = false
                };
                if (ofd.ShowDialog() != true) return;

                string filePath = ofd.FileName;

                bool ok = await ProcessManager.RunAsync("Importing Asset Dump", async h =>
                {
                    await Task.Run(() =>
                    {
                        h.Token.ThrowIfCancellationRequested();
                        h.Report(0, $"Importing {asset.Name}...");

                        if (!ImportSingleAssetDump(container, filePath, out string? error))
                            throw new InvalidOperationException(error ?? "Unknown import error.");

                        h.Report(100, "Dump imported successfully.");
                    }, h.Token);
                });

                if (ok)
                    NotificationManager.Success(asset.Name, "Dump imported");
                return;
            }

            // Case 2: Batch
            var dirDlg = new OpenFolderDialog { Title = "Select directory containing JSON dumps" };
            if (dirDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dirDlg.FolderName))
                return;

            string directory = dirDlg.FolderName;
            var selectedContainers = SelectedItems
                .Select(x => x.assetContainer)
                .OfType<AssetContainer>()
                .ToList();

            var extensions = new List<string> { "json", "txt" };

            var vm = new BatchImportViewModel(workspace, selectedContainers, directory, extensions);
            var view = new BatchImportView(vm) { Owner = Application.Current.MainWindow };

            if (view.ShowDialog() != true)
                return;

            if (vm.ConfirmedImports.Count == 0)
            {
                NotificationManager.Info("No matching dump files found for the selected assets.",
                    "Import Dump", key: "import-dump-none");
                return;
            }

            var importTargets = vm.ConfirmedImports.ToList();
            int total = importTargets.Count;
            int imported = 0, failed = 0;

            bool completed = await ProcessManager.RunAsync("Batch Importing Asset Dumps", async h =>
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        h.Token.ThrowIfCancellationRequested();

                        var batchInfo = importTargets[i];
                        h.Report((i + 1) * 100.0 / total, $"Importing ({i + 1}/{total}): {batchInfo.AssetName}");

                        try
                        {
                            if (ImportSingleAssetDump(batchInfo.Container, batchInfo.ImportFilePath, out string? error))
                            {
                                imported++;
                            }
                            else
                            {
                                failed++;
                                Status.Write($"Dump import failed for {batchInfo.AssetName}: {error}", StatusType.Warning);
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            Status.Write($"Dump import exception for {batchInfo.AssetName}: {ex.Message}", StatusType.Error);
                        }
                    }
                    h.Report(100, $"Finished: {imported}/{total} dumps imported.");
                }, h.Token);
            });

            if (!completed)
                NotificationManager.Warning($"Stopped after {imported}/{total} dumps.",
                    "Batch dump import cancelled", key: "import-dump-batch");
            else if (failed == 0)
                NotificationManager.Success($"Imported {imported}/{total} dumps", "Batch dump import completed",
                    key: "import-dump-batch");
            else
                NotificationManager.Warning($"{imported} imported, {failed} failed. Check the console.",
                    "Batch dump import finished with errors", durationMs: 0, key: "import-dump-batch");
        }

        private bool ImportSingleAssetDump(AssetContainer container, string filePath, out string? errorMessage)
        {
            errorMessage = null;

            if (container == null || container.FileInstance == null)
            {
                errorMessage = "Container or file instance is null.";
                return false;
            }

            try
            {
                AssetTypeTemplateField tempField = workspace.GetTemplateField(container);
                if (tempField == null)
                {
                    errorMessage = $"Could not obtain template field for type {container.TypeId}.";
                    return false;
                }

                RefTypeManager? refMan = null;
                if (container.TypeId == (int)AssetClassID.MonoBehaviour || container.TypeId < 0)
                    refMan = workspace.am.GetRefTypeManager(container.FileInstance);

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192);
                using var sr = new StreamReader(fs, Encoding.UTF8);

                using var importer = new AssetImportExport(sr, leaveOpen: true);
                byte[]? newBytes = importer.ImportJsonAsset(tempField, refMan, out errorMessage);

                if (newBytes == null || !string.IsNullOrEmpty(errorMessage))
                    return false;

                workspace.AddOrUpdateAsset(container, newBytes);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
        #endregion

        #region Remove Assets
        [RelayCommand]
        private async Task RemoveAsset()
        {
            if (SelectedItems == null || SelectedItems.Count == 0)
            {
                Status.Write("Please select at least one asset to remove.", StatusType.Warning);
                return;
            }

            int count = SelectedItems.Count;
            string confirmMessage = count == 1
                ? $"Are you sure you want to remove '{SelectedItems[0].Name}' (PathID: {SelectedItems[0].PathID})?\n\nThis will mark the asset as removed from the workspace metadata."
                : $"Are you sure you want to remove {count} selected assets?\n\nThis will mark all selected assets as removed from the workspace metadata.";

            bool confirmed = ConfirmationDialog.Show("Confirm Asset Removal", confirmMessage);
            if (!confirmed)
                return;

            var itemsToRemove = SelectedItems.ToList();

            await ProcessManager.RunAsync("Removing Assets", async h =>
            {
                await Task.Run(() =>
                {
                    int removedCount = 0;
                    int total = itemsToRemove.Count;

                    for (int i = 0; i < total; i++)
                    {
                        if (h.Token.IsCancellationRequested)
                            break;

                        var item = itemsToRemove[i];
                        var container = item.assetContainer;

                        if (container != null && container.FileInstance != null)
                        {
                            int progress = (int)(((i + 1) / (float)total) * 100);
                            h.Report(progress, $"Removing ({i + 1}/{total}): {item.Name}");

                            workspace.RemoveAsset(container, container.FileInstance);
                            removedCount++;
                        }
                    }

                    h.Report(100, $"Finished: {removedCount} asset(s) removed.");
                }, h.Token);
            });

            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var item in itemsToRemove)
                {
                    if (item.assetContainer != null)
                        assetContainerMap.Remove(item.assetContainer);
                }

                AssetObjects.RemoveRange(itemsToRemove);

                SelectedItems.Clear();
                SelectedCounts = 0;
                ActiveAssetObject = null;
            });
        }
        #endregion

        #region Filter

        private List<FilterRule> _filterRules = new();
        private bool _filterMatchAll = true;

        [ObservableProperty]
        private bool isFilterActive;

        public void OpenFilter()
        {
            var vm = FilterEditorViewModel.For(UnityAssetFilterDefinition.Fields, _filterRules, _filterMatchAll);

            vm.ApplyRequested += v => ApplyFilter(v.GetActiveRules(), v.MatchAllRules);

            var win = new FilterWindow(vm) { Owner = Application.Current.MainWindow };

            if (win.ShowDialog() == true)
            {
                _filterRules = vm.Snapshot();
                _filterMatchAll = vm.MatchAllRules;
            }
            else
            {
                ApplyFilter(_filterRules, _filterMatchAll);
            }
        }

        // Updates grid filter predicate from active rules; clears filtering if none match.
        private void ApplyFilter(List<FilterRule> rules, bool matchAll)
        {
            var pred = FilterEngine.Build<UnityAssetObjectModel>(rules, matchAll);
            var view = CollectionViewSource.GetDefaultView(AssetObjects);

            view.Filter = pred == null ? null : o => pred((UnityAssetObjectModel)o);
            IsFilterActive = pred != null;

            if (pred != null)
            {
                int shown = AssetObjects.Count(o => pred(o));
                Status.Write($"[Filter] {shown} / {AssetObjects.Count} assets match.", StatusType.Info);
            }
        }

        public void ClearFilter()
        {
            _filterRules = new List<FilterRule>();
            _filterMatchAll = true;
            ApplyFilter(_filterRules, _filterMatchAll);
        }

        #endregion

        [RelayCommand]
        private async Task EditData()
        {
            if (SelectedItems == null || SelectedItems.Count == 0)
            {
                NotificationManager.Warning("Please select at least one asset to edit.", "No Asset Selected");
                return;
            }

            if (workspace == null)
            {
                NotificationManager.Error("Workspace is not initialized.", "Workspace Error");
                return;
            }

            var containersToEdit = SelectedItems
                .Where(item => item?.assetContainer != null)
                .Select(item => item.assetContainer!)
                .ToList();

            if (containersToEdit.Count == 0)
            {
                NotificationManager.Warning("Selected items do not contain editable asset data.", "Invalid Selection");
                return;
            }

            try
            {
                string taskTitle = containersToEdit.Count == 1
                    ? $"Exporting {SelectedItems[0].Name} for editing..."
                    : $"Exporting {containersToEdit.Count} assets for editing...";

                await ProcessManager.RunAsync(taskTitle, async h =>
                {
                    await DataEditor.OpenEditorAsync(containersToEdit, workspace);
                });
            }
            catch (Exception ex)
            {
                Status.Write($"[Editor] Failed to open editor: {ex.Message}", StatusType.Error);
                NotificationManager.Error(ex.Message, "Editor Error");
            }
        }

        [RelayCommand]
        private void ViewData()
        {
            if (SelectedItems == null || SelectedItems.Count == 0)
            {
                NotificationManager.Warning("Please select at least one asset to View.", "No Asset Selected");
                return;
            }

            if (workspace == null)
            {
                NotificationManager.Error("Workspace is not initialized.", "Workspace Error");
                return;
            }

            if (ActiveAssetObject == null || ActiveAssetObject.assetContainer == null)
            {
                NotificationManager.Error("Something went wrong", "null info");
                return;
            }

            new UnityAssetDataViewer(workspace, ActiveAssetObject.assetContainer).Show();
        }

        public override void OnClose()
        {
            workspace.FileLoaded -= OnWorkspaceFileLoaded;
            workspace.AssetUpdated -= OnWorkspaceAssetUpdated;
            workspace.AssetRemoved -= OnWorkspaceAssetRemoved;
            workspace.MarkModified -= OnWorkspaceMarkModified;
            workspace.CheckAssetExistRequested -= OnWorkspaceCheckAssetExist;
            workspace.GoToAssetRequested -= OnWorkspaceGoToAsset;

            lock (_containerLock)
            {
                _containerTool.AssetMap.Clear();
                _containerTool.PreloadTable.Clear();
                _rsrcManLoadedDirs.Clear();
            }

            AssetNameUtils.ClearTemplateCache();

            _pendingInst = null;
            workspace.Dispose();
            CollectionViewSource.GetDefaultView(AssetObjects).Filter = null;
            _gridRef = null;
            base.OnClose();
        }

        public override async Task<bool> OnSaveAsync(bool saveAs = false)
        {
            if (workspace.LoadedFiles.Count == 0)
            {
                NotificationManager.Warning("No assets files are loaded in workspace.", "Save");
                return false;
            }

            if (workspace.ChangedFiles.Count == 0)
            {
                NotificationManager.Info("No changes detected to save.", "Save");
                return true;
            }

            // ─────────────────────────────────────────────────────────────
            // CASE 1: BUNDLE INTERNAL ASSET COMMIT
            // ─────────────────────────────────────────────────────────────
            if (workspace.fromBundle && !saveAs)
            {
                var filesToCommit = workspace.ChangedFiles.ToList();

                foreach (var fileInst in filesToCommit)
                {
                    if (fileInst?.file != null)
                    {
                        OnChildFileCommitted?.Invoke(fileInst);
                    }
                }

                workspace.ChangedFiles.Clear();
                workspace.MarkSaved();
                workspace.NewAssets.Clear();
                workspace.RemovedAssets.Clear();
                IsModified = workspace.IsDirty;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var obj in AssetObjects)
                    {
                        if (!string.IsNullOrEmpty(obj.Modified))
                        {
                            obj.ClearModified();
                        }
                    }
                });

                string committedName = filesToCommit.Count == 1
                    ? (filesToCommit[0].name ?? "CAB File")
                    : $"{filesToCommit.Count} internal file(s)";

                NotificationManager.Success("Changes committed to parent bundle!", committedName);
                Status.Write($"[Bundle Child] Committed {committedName} to parent bundle replacers.", StatusType.Success);
                return true;
            }

            // ─────────────────────────────────────────────────────────────
            // CASE 2: STANDALONE DISK SAVE
            // ─────────────────────────────────────────────────────────────
            var filesToSave = workspace.ChangedFiles.ToList();
            var fileTargetMap = new Dictionary<AssetsFileInstance, string>();

            if (saveAs)
            {
                if (filesToSave.Count == 1)
                {
                    var primaryFile = filesToSave[0];
                    var sfd = new SaveFileDialog
                    {
                        Title = "Save Assets File As",
                        FileName = Path.GetFileName(primaryFile.path),
                        Filter = "All Files (*.*)|*.*",
                        InitialDirectory = Path.GetDirectoryName(primaryFile.path)
                    };

                    if (sfd.ShowDialog() != true)
                        return false;

                    fileTargetMap[primaryFile] = sfd.FileName;
                }
                else
                {
                    var fbd = new Microsoft.Win32.OpenFolderDialog
                    {
                        Title = $"Select Target Folder to Save {filesToSave.Count} Modified Files",
                        InitialDirectory = Path.GetDirectoryName(filesToSave[0].path)
                    };

                    if (fbd.ShowDialog() != true)
                        return false;

                    string targetFolder = fbd.FolderName;
                    foreach (var inst in filesToSave)
                    {
                        fileTargetMap[inst] = Path.Combine(targetFolder, Path.GetFileName(inst.path));
                    }
                }
            }
            else
            {
                foreach (var inst in filesToSave)
                {
                    fileTargetMap[inst] = inst.path;
                }
            }

            var successfullySavedFiles = new List<AssetsFileInstance>();
            var failedFiles = new List<(string FileName, string Error)>();

            bool completed = await ProcessManager.RunAsync("Saving Assets Files", async h =>
            {
                await Task.Run(() =>
                {
                    int total = filesToSave.Count;

                    for (int i = 0; i < total; i++)
                    {
                        h.Token.ThrowIfCancellationRequested();

                        var inst = filesToSave[i];
                        if (inst?.file == null)
                            continue;

                        string destPath = fileTargetMap[inst];
                        string fileName = Path.GetFileName(destPath);

                        h.Report(((i + 1) * 100.0) / total, $"Saving ({i + 1}/{total}): {fileName}...");

                        string tempFilePath = Path.Combine(
                            workspace.TempFilesDirectory,
                            $"save_{Path.GetFileNameWithoutExtension(destPath)}_{Guid.NewGuid():N}.tmp"
                        );

                        try
                        {
                            using (var fs = File.Open(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                            using (var writer = new AssetsFileWriter(fs))
                            {
                                inst.file.Write(writer);
                            }

                            if (saveAs)
                            {
                                string? targetDir = Path.GetDirectoryName(destPath);
                                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                                {
                                    Directory.CreateDirectory(targetDir);
                                }

                                File.Move(tempFilePath, destPath, overwrite: true);
                            }
                            else
                            {
                                inst.file.Close();
                                File.Move(tempFilePath, destPath, overwrite: true);

                                var newStream = File.Open(destPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                inst.file = new AssetsFile();
                                inst.file.Read(new AssetsFileReader(newStream));
                                inst.file.GenerateQuickLookup();

                                AssetNameUtils.ClearTemplateCache(inst);
                            }

                            successfullySavedFiles.Add(inst);
                        }
                        catch (Exception ex)
                        {
                            failedFiles.Add((fileName, ex.Message));
                            Status.Write($"[Save Error Details - {fileName}]\n{ex}", StatusType.Error);

                            if (File.Exists(tempFilePath))
                            {
                                try { File.Delete(tempFilePath); } catch { }
                            }
                        }
                    }

                    if (!saveAs && successfullySavedFiles.Count > 0)
                    {
                        h.SetIndeterminate("Rebuilding workspace lookups...");
                        workspace.GenerateAssetsFileLookup();
                    }
                }, h.Token);
            });

            // ───────────── Post-Save UI State Synchronization ─────────────
            if (successfullySavedFiles.Count > 0)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var savedPaths = new HashSet<string>(
                        successfullySavedFiles.Select(f => f.path.ToLowerInvariant())
                    );

                    var savedLookup = successfullySavedFiles
                        .ToDictionary(f => f.path.ToLowerInvariant(), f => f);

                    foreach (var obj in AssetObjects)
                    {
                        if (obj.assetContainer?.FileInstance?.path is { } p)
                        {
                            string pLower = p.ToLowerInvariant();

                            if (savedPaths.Contains(pLower))
                            {
                                if (!string.IsNullOrEmpty(obj.Modified))
                                {
                                    obj.ClearModified();
                                }

                                if (!saveAs && savedLookup.TryGetValue(pLower, out var matchingInst))
                                {
                                    obj.assetContainer.RefreshFromSavedFile(matchingInst);
                                    obj.Size = obj.assetContainer.FinalSize;
                                }
                            }
                        }
                    }
                });

                foreach (var savedInst in successfullySavedFiles)
                {
                    workspace.ChangedFiles.Remove(savedInst);
                }

                if (workspace.ChangedFiles.Count == 0)
                {
                    workspace.MarkSaved();
                    workspace.NewAssets.Clear();
                    workspace.RemovedAssets.Clear();
                }

                IsModified = workspace.IsDirty;
            }

            if (failedFiles.Count > 0)
            {
                string failSummary = string.Join(", ", failedFiles.Select(f => $"{f.FileName} ({f.Error})"));
                Status.Write($"[Save] {failedFiles.Count} file(s) failed: {failSummary}", StatusType.Error);
                NotificationManager.Error($"Failed to save {failedFiles.Count} file(s). Check status log for details.", "Save Failed");
            }

            if (successfullySavedFiles.Count > 0)
            {
                string summary = (saveAs && filesToSave.Count == 1)
                    ? Path.GetFileName(fileTargetMap[filesToSave[0]])
                    : $"{successfullySavedFiles.Count} of {filesToSave.Count} file(s) saved";

                NotificationManager.Success("Save operation completed!", summary);
            }

            return successfullySavedFiles.Count > 0;
        }
    }
}