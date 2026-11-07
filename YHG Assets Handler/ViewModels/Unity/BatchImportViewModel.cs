using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using YHG_Assets_Handler.Models.Unity;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.ViewModels.Unity
{
    public partial class BatchImportViewModel : ObservableObject
    {
        private readonly UnityAssetWorkspace _workspace;
        private readonly string _targetDirectory;

        public List<ImportBatchInfo> ConfirmedImports { get; } = [];

        [ObservableProperty]
        private ObservableCollection<ImportBatchItem> _matchingAssetItems = [];

        [ObservableProperty]
        private ImportBatchItem? _selectedMatchingItem;

        [ObservableProperty]
        private string? _selectedFileToImport;

        // Dialog close action to link with View window
        public Action<bool>? RequestClose { get; set; }

        public BatchImportViewModel(
            UnityAssetWorkspace workspace,
            IEnumerable<AssetContainer> selection,
            string targetDirectory,
            List<string> targetExtensions)
        {
            _workspace = workspace;
            _targetDirectory = targetDirectory;

            PopulateMatches(selection, targetExtensions);
        }

        private void PopulateMatches(IEnumerable<AssetContainer> selection, List<string> extensions)
        {
            if (!Directory.Exists(_targetDirectory))
                return;

            bool matchAnyExt = extensions.Contains("*");
            var allDirFiles = Directory.GetFiles(_targetDirectory).ToList();

            foreach (var cont in selection)
            {
                AssetNameUtils.GetDisplayNameFast(_workspace, cont, true, out string assetName, out string _);
                string fileInstanceName = Path.GetFileName(cont.FileInstance.path);

                var info = new ImportBatchInfo
                {
                    AssetName = assetName,
                    AssetFile = fileInstanceName,
                    PathId = cont.PathId,
                    Container = cont
                };

                var item = new ImportBatchItem(info);

                List<string> matchedFilenames = matchAnyExt
                    ? allDirFiles
                        .Where(f => Path.GetFileNameWithoutExtension(f).EndsWith(item.GetMatchSuffix("*"), StringComparison.OrdinalIgnoreCase))
                        .Select(Path.GetFileName)
                        .OfType<string>()
                        .ToList()
                    : allDirFiles
                        .Where(f => extensions.Any(ext => f.EndsWith(item.GetMatchSuffix(ext), StringComparison.OrdinalIgnoreCase)))
                        .Select(Path.GetFileName)
                        .OfType<string>()
                        .ToList();

                if (matchedFilenames.Count > 0)
                {
                    item.MatchingFiles = new ObservableCollection<string>(matchedFilenames);
                    item.SelectedIndex = 0;
                    MatchingAssetItems.Add(item);
                }
            }

            if (MatchingAssetItems.Count > 0)
            {
                SelectedMatchingItem = MatchingAssetItems[0];
            }
        }

        partial void OnSelectedMatchingItemChanged(ImportBatchItem? value)
        {
            if (value != null && value.SelectedIndex >= 0 && value.SelectedIndex < value.MatchingFiles.Count)
            {
                SelectedFileToImport = value.MatchingFiles[value.SelectedIndex];
            }
            else
            {
                SelectedFileToImport = null;
            }
        }

        partial void OnSelectedFileToImportChanged(string? value)
        {
            if (SelectedMatchingItem != null && value != null)
            {
                int index = SelectedMatchingItem.MatchingFiles.IndexOf(value);
                if (index != -1)
                {
                    SelectedMatchingItem.SelectedIndex = index;
                }
            }
        }

        [RelayCommand]
        private void EditMatchFile()
        {
            if (SelectedMatchingItem == null) return;

            var ofd = new OpenFileDialog
            {
                Title = "Select Custom File for Asset Import",
                Filter = "All Files (*.*)|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() == true)
            {
                string selectedPath = ofd.FileName;
                SelectedMatchingItem.MatchingFiles.Clear();
                SelectedMatchingItem.MatchingFiles.Add(selectedPath);
                SelectedMatchingItem.SelectedIndex = 0;
                SelectedFileToImport = selectedPath;
            }
        }

        [RelayCommand]
        private void Ok()
        {
            ConfirmedImports.Clear();

            foreach (var item in MatchingAssetItems)
            {
                if (item.SelectedIndex != -1 && item.SelectedIndex < item.MatchingFiles.Count)
                {
                    string fileName = item.MatchingFiles[item.SelectedIndex];
                    string fullPath = Path.IsPathRooted(fileName)
                        ? fileName
                        : Path.Combine(_targetDirectory, fileName);

                    var batchInfo = item.Info;
                    batchInfo.ImportFilePath = fullPath;
                    ConfirmedImports.Add(batchInfo);
                }
            }

            if (ConfirmedImports.Count > 0)
            {
                RequestClose?.Invoke(true);
            }
            else
            {
                RequestClose?.Invoke(false);
            }
        }

        [RelayCommand]
        private void Cancel()
        {
            RequestClose?.Invoke(false);
        }
    }
}