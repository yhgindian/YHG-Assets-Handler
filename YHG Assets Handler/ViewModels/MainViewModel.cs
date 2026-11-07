using AssetsHandler.Controls;
using AssetsHandler.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Views;
using YHG_Assets_Handler.Views.Windows;
using YHG_Assets_Handler.Views.Windows.Tools;
using YHG_Assets_Handler.Views.Windows.Unity;
using YHG_Assets_Handler.Views.Windows.Updater;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<ExplorerItem> rootDirectoryItems = new();

        [ObservableProperty]
        private object? currentWorkspaceView;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
        private ExplorerItem? currentExplorerItem;

        public MainViewModel()
        {
#if DEBUG
            string defaultPath = @"D:\Downloads\Edit\Editors\obb\V4.6\Data\368d36fb049e92a4199b78fa16cb0931";
            
                LoadFileDirectly(defaultPath);
                LoadFileDirectly(@"D:\Downloads\Edit\Editors\obb\V4.6\Data\e24cbc9c5348ecd4bafe1745dd04dd41");
                LoadFileDirectly(@"D:\Downloads\Edit\Editors\obb\V4.6\YHGIndian\maps\sumatera\2006546\2006546\assets\assetpack\sumatera");
            
#endif
        }

        // ───────────── Item Changed & Dynamic Command Update ─────────────

        partial void OnCurrentExplorerItemChanged(ExplorerItem? oldValue, ExplorerItem? newValue)
        {
            if (oldValue?.CachedViewModel != null)
            {
                oldValue.CachedViewModel.PropertyChanged -= OnCachedViewModelPropertyChanged;
            }

            if (newValue?.CachedViewModel != null)
            {
                newValue.CachedViewModel.PropertyChanged += OnCachedViewModelPropertyChanged;
            }

            SaveCommand.NotifyCanExecuteChanged();
            SaveAsCommand.NotifyCanExecuteChanged();
        }

        private void OnCachedViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BaseViewModel.IsModified))
            {
                SaveCommand.NotifyCanExecuteChanged();
            }
        }

        [RelayCommand]
        private void SelectItem(ExplorerItem? item)
        {
            OnItemSelected(item);
        }

        // ───────────── File & Folder Open ─────────────

        [RelayCommand]
        private void OpenAsset()
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Asset Files|*.*",
                Title = "Open Asset File",
                Multiselect = true
            };

            if (ofd.ShowDialog() == true)
            {
                foreach (var filePath in ofd.FileNames)
                    RootDirectoryItems.Add(LoadFile(filePath));

                Status.Write($"Opened {ofd.FileNames.Length} file(s).", StatusType.Success);
            }
        }

        [RelayCommand]
        private void OpenFolder()
        {
            var ofd = new OpenFolderDialog { Title = "Open Asset Folder" };

            if (ofd.ShowDialog() == true)
            {
                var rootItem = LoadDirectory(ofd.FolderName);
                if (rootItem != null)
                    RootDirectoryItems.Add(rootItem);

                Status.Write($"Loaded folder: {ofd.FolderName}", StatusType.Success);
            }
        }

        [RelayCommand]
        private void Exit() => Application.Current.Shutdown();

        // ───────────── Save & Save As Commands ─────────────

        private bool CanSave() => CurrentExplorerItem?.CachedViewModel?.IsModified == true;
        private bool CanSaveAs() => CurrentExplorerItem?.CachedViewModel != null;

        [RelayCommand(CanExecute = nameof(CanSave))]
        private async Task SaveAsync()
        {
            if (CurrentExplorerItem?.CachedViewModel is { } vm)
            {
                try
                {
                    bool ok = await vm.OnSaveAsync(saveAs: false);
                    if (ok)
                    {
                        Status.Write($"Saved: {CurrentExplorerItem.Name}", StatusType.Success);
                        SaveCommand.NotifyCanExecuteChanged();
                    }
                }
                catch (Exception ex)
                {
                    Status.Write($"Failed to save {CurrentExplorerItem.Name}: {ex.Message}", StatusType.Error);
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanSaveAs))]
        private async Task SaveAsAsync()
        {
            if (CurrentExplorerItem?.CachedViewModel is { } vm)
            {
                try
                {
                    bool ok = await vm.OnSaveAsync(saveAs: true);
                    if (ok)
                    {
                        Status.Write($"Saved As: {CurrentExplorerItem.Name}", StatusType.Success);
                        SaveCommand.NotifyCanExecuteChanged();
                    }
                }
                catch (Exception ex)
                {
                    Status.Write($"Failed to Save As {CurrentExplorerItem.Name}: {ex.Message}", StatusType.Error);
                }
            }
        }

        private bool CanCloseSPecific() => CurrentExplorerItem != null;
        [RelayCommand(CanExecute = nameof(CanCloseSPecific))]
        public async Task CloseItem(ExplorerItem? targetItem)
        {
            targetItem ??= CurrentExplorerItem;
            if (targetItem == null) return;

            if (targetItem.Parent != null)
            {
                if (TryGetViewModel<UnityBundleInspectorViewModel>(out var s, targetItem.Parent))
                {
                    return;   
                }
            }

            if (targetItem.CachedViewModel is { } vm)
            {
                bool canClose = await vm.CanCloseAsync();
                if (!canClose)
                    return;

                vm.Dispose();
                targetItem.CachedViewModel = null;
            }

            if (CurrentExplorerItem == targetItem)
            {
                CurrentWorkspaceView = null;
                CurrentExplorerItem = null;
            }

            RemoveItemRecursive(RootDirectoryItems, targetItem);

            Status.Write($"Closed: {targetItem.Name}", StatusType.Info);
            SaveCommand.NotifyCanExecuteChanged();
        }

        private static bool RemoveItemRecursive(IList<ExplorerItem> collection, ExplorerItem target)
        {
            if (collection.Remove(target)) return true;

            foreach (var item in collection)
            {
                if (item.Children.Count > 0 && RemoveItemRecursive(item.Children, target))
                    return true;
            }
            return false;
        }

        private bool CanCloseAll() => RootDirectoryItems.Count > 0;

        [RelayCommand(CanExecute = nameof(CanCloseAll))]
        private async Task CloseAllAsync()
        {
            var allVMs = GetAllCachedViewModels(RootDirectoryItems);
            var modifiedItems = allVMs.Where(v => v.IsModified).ToList();

            if (modifiedItems.Count > 0)
            {
                string list = string.Join("\n", modifiedItems.Take(5).Select(v => $"• {v.DisplayName}"));
                if (modifiedItems.Count > 5)
                    list += $"\n...and {modifiedItems.Count - 5} more";

                bool proceed = ConfirmationDialog.Show(
                    "Unsaved Changes",
                    $"The following workspace(s) have unsaved modifications:\n\n{list}\n\nDo you want to discard changes and close all?");

                if (!proceed) return;
            }

            foreach (var vm in allVMs)
            {
                try
                {
                    vm.Dispose();
                }
                catch
                {
                }
            }

            RootDirectoryItems.Clear();
            CurrentWorkspaceView = null;
            CurrentExplorerItem = null;

            SaveCommand.NotifyCanExecuteChanged();
            SaveAsCommand.NotifyCanExecuteChanged();
            CloseAllCommand.NotifyCanExecuteChanged();

            Status.Write("All assets and workspaces closed.", StatusType.Info);
        }

        [RelayCommand]
        private void RevealInExplorer(ExplorerItem? item)
        {
            item ??= CurrentExplorerItem;
            if (item == null || string.IsNullOrWhiteSpace(item.FullPath)) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Status.Write($"Failed to reveal in Explorer: {ex.Message}", StatusType.Error);
            }
        }


        [RelayCommand]
        private void AddAsset()
        {
            if (CurrentExplorerItem?.FileType == FileType.UnityAsset)
            {
                if (CurrentWorkspaceView is UnityAssetsInspectorViewModel unityVm)
                {
                    var workspace = unityVm.workspace;
                    var dialog = new AddAssetView(workspace)
                    {
                        Owner = Application.Current.MainWindow
                    };
                    if (dialog.ShowDialog() == true)
                    {
                        Status.Write($"Asset created successfully in {CurrentExplorerItem.Name}", StatusType.Success);
                        SaveCommand.NotifyCanExecuteChanged();
                    }
                }
            }
        }
        // ───────────── Loading ─────────────

        public void LoadFileDirectly(string filePath)
        {
            if (File.Exists(filePath))
            {
                RootDirectoryItems.Add(LoadFile(filePath));
            }
        }

        private ExplorerItem LoadFile(string filePath)
        {
            return new ExplorerItem
            {
                Name = Path.GetFileName(filePath),
                FullPath = filePath,
                IsFolder = false,
            };
        }

        private ExplorerItem LoadDirectory(string folderPath)
        {
            string folderName = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(folderName))
                folderName = folderPath;

            var item = new ExplorerItem
            {
                Name = folderName,
                FullPath = folderPath,
                IsFolder = true
            };

            try
            {
                foreach (string subDir in Directory.EnumerateDirectories(folderPath))
                    item.Children.Add(LoadDirectory(subDir));

                foreach (string file in Directory.EnumerateFiles(folderPath))
                {
                    if (UnityAssetWorkspace.IsSplitFile(file)) continue;
                    item.Children.Add(LoadFile(file));
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip protected paths
            }

            return item;
        }

        // ───────────── Selection ─────────────

        public void OnItemSelected(ExplorerItem? item)
        {
            if (item == null || item.IsFolder)
            {
                CurrentWorkspaceView = null;
                CurrentExplorerItem = null;
                return;
            }

            item.CachedViewModel ??= CreateViewModel(item);
            (item.CachedViewModel as UnityAssetsInspectorViewModel)?.EnsureLoaded();   // bundle child lazy load
            CurrentWorkspaceView = item.CachedViewModel;
            CurrentExplorerItem = item;
        }

        public BaseViewModel? CreateViewModel(ExplorerItem item)
        {
            return item.FileType switch
            {
                FileType.UnityAsset => new UnityAssetsInspectorViewModel(item),
                FileType.UnityBundle => new UnityBundleInspectorViewModel(item),
                _ => new DefaultInspectorViewModel(item),
            };
        }

        // ───────────── Closing & Teardown ─────────────

        public async Task<bool> CanCloseAppAsync()
        {
            var allViewModels = GetAllCachedViewModels(RootDirectoryItems);

            var modifiedItems = allViewModels.Where(v => v.IsModified).ToList();

            if (modifiedItems.Count > 0)
            {
                string list = string.Join("\n", modifiedItems.Take(5).Select(v => $"• {v.DisplayName}"));
                if (modifiedItems.Count > 5)
                    list += $"\n...and {modifiedItems.Count - 5} more";

                bool proceed = ConfirmationDialog.Show(
                    "Unsaved Changes",
                    $"The following workspace(s) have unsaved modifications:\n\n{list}\n\nDo you want to discard changes and exit?"
                );

                if (!proceed) return false;

                return true;
            }

            foreach (var vm in allViewModels)
            {
                if (!await vm.CanCloseAsync())
                    return false;
            }

            return true;
        }

        public void OnAppClosing()
        {
            var allViewModels = GetAllCachedViewModels(RootDirectoryItems);
            foreach (var vm in allViewModels)
            {
                try
                {
                    vm.Dispose();
                }
                catch
                {
                    // Isolated file errors won't block app shutdown
                }
            }
        }

        private static List<BaseViewModel> GetAllCachedViewModels(IEnumerable<ExplorerItem> items)
        {
            var list = new List<BaseViewModel>();
            foreach (var item in items)
            {
                if (item.CachedViewModel != null)
                    list.Add(item.CachedViewModel);

                if (item.Children.Count > 0)
                    list.AddRange(GetAllCachedViewModels(item.Children));
            }
            return list;
        }

        [RelayCommand]
        private void AssetFileInfos()
        {
            if (TryGetViewModel(out UnityAssetsInspectorViewModel? vm))
            {
                UnityAssetFilesInfos f = new(vm.workspace);
                f.ShowDialog();
            }
        }

        [RelayCommand]
        private async Task AddFilesToWorkspace()
        {
            if (!TryGetViewModel(out UnityAssetsInspectorViewModel? vm))
                return;

            var ofd = new OpenFileDialog
            {
                Title = "Select Assets Files to Add to Workspace",
                Filter = "Unity Assets Files (*.assets;*.*)|*.assets;*.*",
                Multiselect = true
            };

            if (ofd.ShowDialog() != true || ofd.FileNames.Length == 0)
                return;

            int loaded = await vm.LoadFilesAsync(ofd.FileNames, "Adding Files to Workspace");
            Status.Write($"Added {loaded}/{ofd.FileNames.Length} file(s) to workspace.",
                loaded > 0 ? StatusType.Success : StatusType.Warning);
        }

        private bool TryGetViewModel<T>([NotNullWhen(true)] out T? vm, ExplorerItem? explorer = null) where T : BaseViewModel
        {
            explorer ??= CurrentExplorerItem;

            if (explorer?.CachedViewModel is T match)
            {
                vm = match;
                return true;
            }

            vm = default;
            return false;
        }

        [RelayCommand]
        private void SetupMonoGenerator()
        {
            if (TryGetViewModel(out UnityAssetsInspectorViewModel? vm))
            {
                vm.workspace.SetMonoTempGenerators(string.Empty);
            }
        }

        [RelayCommand]
        private void UnityAssetsSplitMerge()
        {
            WindowHelper.ShowSingle<UnityAssetsMergeSplitter>();
        }

        [RelayCommand]
        private void DataGridFilter()
        {
            if (TryGetViewModel(out UnityAssetsInspectorViewModel? vm))
                vm.OpenFilter();
            else
                NotificationManager.Info("Open an assets file first to use the filter.", "Filter",
                    key: "filter-no-workspace");
        }

        [RelayCommand]
        private void AboutApp()
        {
            WindowHelper.ShowSingle<About>();
        }

        [RelayCommand]
        private void CheckForUpdate()
        {
            WindowHelper.ShowSingle<Updater>();
        }

        [RelayCommand]
        private void Donation()
        {
            WindowHelper.ShowSingle<DonationWindow>();
        }
    }
}