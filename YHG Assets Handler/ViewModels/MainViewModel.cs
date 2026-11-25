using AssetsHandler.Controls;
using AssetsHandler.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PluginAbstractions;
using YHG_Assets_Handler.Plugins;
using YHG_Assets_Handler.Views;
using YHG_Assets_Handler.Views.Windows;
using YHG_Assets_Handler.Views.Windows.Updater;

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
        }

        // ───────────── Plugin menu contributions ─────────────

        private readonly List<AsyncRelayCommand> _pluginCommands = new();

        public MenuContext BuildMenuContext() => new()
        {
            CurrentItem = CurrentExplorerItem,
            Owner = Application.Current?.MainWindow
        };

        /// <summary>Creates the command behind a plugin menu entry. Host never looks inside the plugin's action.</summary>
        public ICommand CreatePluginCommand(MenuContribution c)
        {
            var cmd = new AsyncRelayCommand(
                async () =>
                {
                    try
                    {
                        var ctx = BuildMenuContext();
                        if (c.ExecuteAsync != null) await c.ExecuteAsync(ctx);
                        else c.Execute?.Invoke(ctx);
                        SaveCommand.NotifyCanExecuteChanged();
                    }
                    catch (Exception ex)
                    {
                        Status.Write($"{c.Header.Replace("_", "")} failed: {ex.Message}", StatusType.Error);
                    }
                },
                () =>
                {
                    try { return c.CanExecute?.Invoke(BuildMenuContext()) ?? true; }
                    catch { return false; }
                });
            _pluginCommands.Add(cmd);
            return cmd;
        }

        private void RefreshPluginCommands()
        {
            foreach (var c in _pluginCommands) c.NotifyCanExecuteChanged();
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
            RefreshPluginCommands();
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

            // Virtual children (e.g. entries inside a container file) are closed with their parent
            if (targetItem.Parent != null && !targetItem.CanCloseIndependently)
                return;

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
                    if (PluginManager.Instance.IsHidden(file)) continue;
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
            item.CachedViewModel?.OnActivated();   // lazy-load hook (plugins decide what it does)
            CurrentWorkspaceView = item.CachedViewModel;
            CurrentExplorerItem = item;
        }

        public BaseViewModel? CreateViewModel(ExplorerItem item)
        {
            var result = PluginManager.Instance.CreateViewModel(item);
            if (result.Error != null)
                Status.Write($"Failed to open {item.Name}: {result.Error}", StatusType.Error);

            return result.ViewModel ?? new DefaultInspectorViewModel(item);
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
        private void OpenPluginManager()
        {
            WindowHelper.ShowSingle<PluginManagerWindow>();
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
