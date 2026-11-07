using AssetsTools.NET;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using YHG_Assets_Handler.Models;

namespace YHG_Assets_Handler.Workspace.UnityBundle
{
    public partial class UnityBundleContainer : ObservableObject
    {
        public AssetBundleDirectoryInfo DirectoryInfo { get; private set; }
        public string OriginalName { get; }

        public ExplorerItem? AssociatedItem { get; set; }

        [ObservableProperty]
        private int _index;

        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private long _size;

        [ObservableProperty]
        private string _modified = string.Empty;

        [ObservableProperty]
        private bool _isNew;

        [ObservableProperty]
        private bool _isSerialized;

        [ObservableProperty]
        private bool _isModified;

        public UnityBundleContainer(AssetBundleDirectoryInfo directoryInfo, bool isNew = false, int index = 0)
        {
            DirectoryInfo = directoryInfo ?? throw new ArgumentNullException(nameof(directoryInfo));
            _index = index;
            OriginalName = directoryInfo.Name ?? string.Empty;
            _name = directoryInfo.Name ?? string.Empty;
            _size = directoryInfo.DecompressedSize;
            _isNew = isNew;

            // Flags bit 0x04 = serialized .assets / CAB
            _isSerialized = (directoryInfo.Flags & 0x04) != 0;
            IsModified = isNew;
        }

        partial void OnIsModifiedChanged(bool value)
        {
            Modified = value ? "*" : string.Empty;
        }

        partial void OnNameChanged(string value)
        {
            DirectoryInfo.Name = value;

            if (!_isNew)
            {
                IsModified = !string.Equals(value, OriginalName, StringComparison.Ordinal);
            }

            if (AssociatedItem != null)
            {
                AssociatedItem.Name = value;
            }
        }

        public void UpdateDirectoryInfo(AssetBundleDirectoryInfo newDirInfo)
        {
            DirectoryInfo = newDirInfo ?? throw new ArgumentNullException(nameof(newDirInfo));
            Size = newDirInfo.DecompressedSize;
            IsSerialized = (newDirInfo.Flags & 0x04) != 0;
            Name = newDirInfo.Name ?? string.Empty;
        }
    }
}