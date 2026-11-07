using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Texture2D
{
    public sealed class Texture2DEditPlugin : IInternalAssetPlugin
    {
        public string DisplayName => "Edit Texture2D";
        public int TargetClassId => 28;

        public PluginTargetMode TargetMode => PluginTargetMode.Any;

        public Task ExecuteAsync(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets)
        {
            if (selectedAssets == null || selectedAssets.Count == 0)
                return Task.CompletedTask;

            var dialog = new EditTextureView(workspace, selectedAssets)
            {
                Owner = Application.Current.MainWindow
            };

            dialog.ShowDialog();
            return Task.CompletedTask;
        }
    }
}