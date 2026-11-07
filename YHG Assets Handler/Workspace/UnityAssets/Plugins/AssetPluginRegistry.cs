using YHG_Assets_Handler.Workspace.UnityAssets.Plugins.AudioClip;
using YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Font;
using YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Mesh;
using YHG_Assets_Handler.Workspace.UnityAssets.Plugins.TextAsset;
using YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Texture2D;
using YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Transform;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins
{
    public static class AssetPluginRegistry
    {
        public static readonly IInternalAssetPlugin[] Plugins =
        [
            // Texture 2D
            new TextureExportPlugin(),
            new Texture2DEditPlugin(),

            // TextAsset
            new TextAssetExportPlugin(),
            new TextAssetImportPlugin(),

            // Font
            new FontExportPlugin(),
            new FontImportPlugin(),

            // AudioClip
            new AudioClipExportPlugin(),
            new AudioClipImportPlugin(),

            // Mesh
            new MeshExportPlugin(),
            new MeshImportPlugin(),

            // Transform
            new TransformEditPlugin(),
        ];
    }
}
