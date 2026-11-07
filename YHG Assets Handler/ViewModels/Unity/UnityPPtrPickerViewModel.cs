using AssetsTools.NET;
using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Media;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.ViewModels.Unity
{
    public class FileTargetItem
    {
        public int FileId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public AssetsFileInstance? FileInstance { get; set; }
        public string? DepPath { get; set; }
        public string DisplayText => $"[{FileId}] {FileName}";
        public override string ToString() => DisplayText;
    }
    public class SimpleAssetItem
    {
        public long PathId { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public string? AssetName { get; set; }
        public AssetContainer? Container { get; set; }
        public string DisplayText => string.IsNullOrWhiteSpace(AssetName)
            ? $"[{PathId}], {TypeName}"
            : $"[{PathId}], {TypeName} \"{AssetName}\"";
        public override string ToString() => DisplayText;
    }
    public partial class UnityPPtrPickerViewModel : ObservableObject
    {
        private readonly UnityAssetWorkspace w;
        private readonly AssetsFileInstance? referenceFrom;
        private readonly HashSet<int>? allowedTypeIds;
        public Action<FileTargetItem, SimpleAssetItem>? OnApply { get; set; }
        public event Action? RequestClose;
        [ObservableProperty]
        private ObservableCollection<FileTargetItem> availableFiles = new();
        [ObservableProperty]
        private FileTargetItem? selectedFileTarget;
        [ObservableProperty]
        private ObservableCollection<SimpleAssetItem> targetAssets = new();
        [ObservableProperty]
        private SimpleAssetItem? selectedAssetModel;
        [ObservableProperty]
        private string verificationText = "No Pointer Selected";
        [ObservableProperty]
        private Brush verificationColor = Brushes.Gray;
        public SimpleAssetItem? Result { get; private set; }
        public UnityPPtrPickerViewModel(
            UnityAssetWorkspace w,
            AssetsFileInstance? reference = null,
            Action<FileTargetItem, SimpleAssetItem>? onApply = null,
            int? defaultFileId = null,
            long? defaultPathId = null,
            params AssetClassID[] allowed)
        {
            this.w = w;
            this.referenceFrom = reference;
            this.OnApply = onApply;
            if (allowed != null && allowed.Length > 0)
            {
                this.allowedTypeIds = new HashSet<int>(allowed.Select(x => (int)x));
            }
            LoadAvailableFiles();
            if (defaultFileId.HasValue)
            {
                var targetFile = AvailableFiles.FirstOrDefault(f => f.FileId == defaultFileId.Value);
                if (targetFile != null)
                {
                    SelectedFileTarget = targetFile;
                }
            }
            else if (AvailableFiles.Count > 0)
            {
                SelectedFileTarget = AvailableFiles[0];
            }
            if (defaultPathId.HasValue && defaultPathId.Value != 0)
            {
                var targetAsset = TargetAssets.FirstOrDefault(a => a.PathId == defaultPathId.Value);
                if (targetAsset != null)
                {
                    SelectedAssetModel = targetAsset;
                }
            }
            ValidateSelection();
        }
        private void LoadAvailableFiles()
        {
            AvailableFiles.Clear();
            if (referenceFrom != null)
            {
                string selfName = Path.GetFileName(referenceFrom.path);
                if (string.IsNullOrEmpty(selfName)) selfName = referenceFrom.name;
                var selfItem = new FileTargetItem
                {
                    FileId = 0,
                    FileName = selfName,
                    FileInstance = referenceFrom
                };
                AvailableFiles.Add(selfItem);
                var externals = referenceFrom.file.Metadata.Externals;
                for (int i = 0; i < externals.Count; i++)
                {
                    int fileId = i + 1;
                    string depPath = externals[i].PathName;
                    string depName = Path.GetFileName(depPath);
                    AvailableFiles.Add(new FileTargetItem
                    {
                        FileId = fileId,
                        FileName = depName,
                        DepPath = depPath,
                        FileInstance = null
                    });
                }
            }
            else
            {
                for (int i = 0; i < w.LoadedFiles.Count; i++)
                {
                    var fileInst = w.LoadedFiles[i];
                    string name = Path.GetFileName(fileInst.path);
                    if (string.IsNullOrEmpty(name)) name = fileInst.name;
                    AvailableFiles.Add(new FileTargetItem
                    {
                        FileId = i,
                        FileName = name,
                        FileInstance = fileInst
                    });
                }
            }
        }
        partial void OnSelectedFileTargetChanged(FileTargetItem? value)
        {
            if (value == null)
            {
                TargetAssets.Clear();
                ValidateSelection();
                return;
            }
            if (value.FileInstance == null && referenceFrom != null)
            {
                try
                {
                    value.FileInstance = AssetContext.GetDepLoad(w, referenceFrom, value.FileId);
                }
                catch
                {
                    value.FileInstance = null;
                }
            }
            LoadAssetsForSelectedFile();
            ValidateSelection();
        }
        partial void OnSelectedAssetModelChanged(SimpleAssetItem? value)
        {
            ValidateSelection();
        }
        private void LoadAssetsForSelectedFile()
        {
            TargetAssets.Clear();
            if (SelectedFileTarget?.FileInstance == null)
            {
                return;
            }
            var containers = w.GetAssetContainersFrom(SelectedFileTarget.FileInstance);
            IEnumerable<AssetContainer> validContainers = containers;
            if (allowedTypeIds != null && allowedTypeIds.Count > 0)
            {
                validContainers = validContainers.Where(c => allowedTypeIds.Contains(c.TypeId));
            }
            foreach (var c in validContainers)
            {
                AssetNameUtils.GetDisplayNameFast(w, c, false, out string name, out string type);
                TargetAssets.Add(new SimpleAssetItem
                {
                    PathId = c.PathId,
                    TypeName = type,
                    AssetName = string.IsNullOrWhiteSpace(name) ? null : name,
                    Container = c
                });
            }
        }
        private void ValidateSelection()
        {
            Result = null;
            if (SelectedFileTarget?.FileInstance == null)
            {
                VerificationText = "File not loaded";
                VerificationColor = Brushes.Crimson;
                ApplyCommand.NotifyCanExecuteChanged();
                return;
            }
            if (SelectedAssetModel?.Container != null)
            {
                VerificationText = string.IsNullOrEmpty(SelectedAssetModel.AssetName)
                    ? $"✓ [{SelectedAssetModel.TypeName}] {SelectedAssetModel.PathId}"
                    : $"✓ [{SelectedAssetModel.TypeName}] {SelectedAssetModel.AssetName}";
                VerificationColor = new SolidColorBrush(Color.FromRgb(78, 201, 176));
                Result = SelectedAssetModel;
                ApplyCommand.NotifyCanExecuteChanged();
                return;
            }
            VerificationText = "No Pointer Selected";
            VerificationColor = Brushes.Gray;
            ApplyCommand.NotifyCanExecuteChanged();
        }
        public bool CanApply() => Result != null && SelectedFileTarget?.FileInstance != null;
        [RelayCommand(CanExecute = nameof(CanApply))]
        private void Apply()
        {
            if (SelectedFileTarget != null && Result != null)
            {
                OnApply?.Invoke(SelectedFileTarget, Result);
                RequestClose?.Invoke();
            }
        }
    }
}