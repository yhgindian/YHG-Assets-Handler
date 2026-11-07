using AssetsTools.NET;
using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.ComponentModel;
using YHG_Assets_Handler.Utils.Unity;

namespace YHG_Assets_Handler.Workspace.UnityAssets
{
    public partial class UnityAssetObjectModel : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _container = string.Empty;

        [ObservableProperty]
        private string _type = string.Empty;

        [ObservableProperty]
        private int _fileID;

        [ObservableProperty]
        private long _pathID;

        [ObservableProperty]
        private uint _size;

        [ObservableProperty]
        private string _modified = string.Empty;

        public AssetClassID TypeClass { get; set; }
        public AssetContainer? assetContainer { get; set; }

        public void RefreshFromContainer(UnityAssetWorkspace workspace)
        {
            if (assetContainer != null)
            {
                Size = assetContainer.FinalSize;
                Modified = "*";
                AssetNameUtils.GetDisplayNameFast(workspace, assetContainer, false, out string newName, out _);
                Name = newName;
            }
        }

        public void ClearModified()
        {
            Modified = string.Empty;
        }
    }
}