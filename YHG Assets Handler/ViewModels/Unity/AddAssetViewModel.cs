using AssetsTools.NET;
using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using YHG_Assets_Handler.Models.Unity;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.Views;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.ViewModels.Unity
{
    public partial class AddAssetViewModel : ObservableObject
    {
        private readonly UnityAssetWorkspace w;
        private readonly AssetsManager manager;

        [ObservableProperty]
        private ObservableCollection<AssetsFileDisplayItem> files = new();

        [ObservableProperty]
        private AssetsFileDisplayItem? selectedFile;

        [ObservableProperty]
        private string pathIdString = string.Empty;

        [ObservableProperty]
        private ObservableCollection<AssetClassDisplayItem> availableClasses = new();

        [ObservableProperty]
        private AssetClassDisplayItem? selectedClass;

        [ObservableProperty]
        private bool createBlankAsset = true;

        public event Action<bool>? RequestClose;

        public AddAssetViewModel(UnityAssetWorkspace workspace, IEnumerable<AssetsFileInstance> fileInsts)
        {
            w = workspace;
            manager = w.am;

            int index = 0;
            foreach (var inst in fileInsts)
            {
                string fileName = !string.IsNullOrEmpty(inst.path)
                    ? Path.GetFileName(inst.path)
                    : (inst.name ?? $"File_{index}");

                Files.Add(new AssetsFileDisplayItem
                {
                    Name = $"[{index++}] {fileName}",
                    Instance = inst
                });
            }

            if (Files.Count > 0)
            {
                SelectedFile = Files[0];
            }

            var sortedClasses = Enum.GetValues(typeof(AssetClassID))
                .Cast<AssetClassID>()
                .Select(c => new AssetClassDisplayItem
                {
                    ClassId = c,
                    Name = c.ToString()
                })
                .OrderBy(c => c.Name)
                .ToList();

            foreach (var item in sortedClasses)
            {
                AvailableClasses.Add(item);
            }

            SelectedClass = AvailableClasses.FirstOrDefault(c => c.ClassId == AssetClassID.Texture2D)
                            ?? AvailableClasses.FirstOrDefault();
        }

        partial void OnSelectedFileChanged(AssetsFileDisplayItem? value)
        {
            if (value?.Instance == null) return;

            var inst = value.Instance;
            long lastId = 0;
            if (inst.file.Metadata.AssetInfos.Count > 0)
            {
                lastId = inst.file.Metadata.AssetInfos.Max(a => a.PathId);
            }

            PathIdString = (lastId + 1).ToString();
        }

        [RelayCommand]
        private void Ok()
        {
            if (SelectedFile?.Instance == null)
            {
                ConfirmationDialog.Show("Selection Error", "Please select a target assets file.");
                return;
            }

            if (SelectedClass == null)
            {
                ConfirmationDialog.Show("Selection Error", "Please select a valid Asset Class.");
                return;
            }

            if (!long.TryParse(PathIdString, out long pathId))
            {
                ConfirmationDialog.Show("Invalid Input", "Path ID must be a valid number (long).");
                return;
            }

            var file = SelectedFile.Instance;
            int typeId = SelectedClass.Id;
            ushort monoId = ushort.MaxValue; // Engine default

            AssetTypeValueField? baseField = null;

            if (CreateBlankAsset)
            {
                try
                {
                    baseField = manager.CreateEmptyBaseField(typeId);
                }
                catch (Exception)
                {
                    baseField = null;
                }
            }

            // Custom implementation to circumvent an upstream AssetsTools.NET deserialization issue
            var createdContainer = w.CreateAsset(file, typeId, pathId, monoId, baseField);

            if (createdContainer == null)
            {
                ConfirmationDialog.Show("Creation Failed", "Failed to register asset. Check log for details.");
                return;
            }

            RequestClose?.Invoke(true);
        }

        [RelayCommand]
        private void Cancel()
        {
            RequestClose?.Invoke(false);
        }
    }
}