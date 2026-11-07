using System.Collections.Generic;
using AssetsHandler.Controls;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Texture2D
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