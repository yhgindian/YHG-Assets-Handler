using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.Text;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Transform
{
    public class TransformEditPlugin : IInternalAssetPlugin
    {
        public string DisplayName => "Edit Transform";
        public int TargetClassId => (int)AssetClassID.Transform; // 4
        public PluginTargetMode TargetMode => PluginTargetMode.Single;

        public async Task ExecuteAsync(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets)
        {
            if (selectedAssets[0].assetContainer is AssetContainer cont)
            {
                EditTransformWindow f = new EditTransformWindow(workspace, cont);
                f.ShowDialog();
            }
        }
    }
}
