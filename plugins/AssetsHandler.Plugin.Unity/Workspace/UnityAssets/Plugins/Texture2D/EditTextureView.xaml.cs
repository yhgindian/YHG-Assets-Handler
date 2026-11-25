using System.Collections.Generic;
using AssetsHandler.Controls;
using AssetsHandler.Plugin.Unity.Models;
using AssetsHandler.Plugin.Unity.Workspace.UnityAssets;

namespace AssetsHandler.Plugin.Unity.Workspace.UnityAssets.Plugins.Texture2D
{
    public partial class EditTextureView : DarkWindow
    {
        public EditTextureView(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets)
        {
            InitializeComponent();

            var vm = new EditTextureViewModel(workspace, selectedAssets);
            DataContext = vm;

            vm.RequestClose += () =>
            {
                Close();
            };
        }
    }
}