using AssetsHandler.Plugin.Unity.Workspace.UnityAssets.Plugins.AudioClip;
using AssetsHandler.Plugin.Unity.Workspace.UnityAssets.Plugins.Font;
using AssetsHandler.Plugin.Unity.Workspace.UnityAssets.Plugins.Mesh;
using AssetsHandler.Plugin.Unity.Workspace.UnityAssets.Plugins.TextAsset;
using AssetsHandler.Plugin.Unity.Workspace.UnityAssets.Plugins.Texture2D;
using AssetsHandler.Plugin.Unity.Workspace.UnityAssets.Plugins.Transform;

namespace AssetsHandler.Plugin.Unity.Workspace.UnityAssets.Plugins
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
