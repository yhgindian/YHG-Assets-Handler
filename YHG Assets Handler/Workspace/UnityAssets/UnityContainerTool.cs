using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using YHG_Assets_Handler.Utils.Unity;

namespace YHG_Assets_Handler.Workspace.UnityAssets
{
    public class UnityContainerTool
    {
        public List<AssetPPtr> PreloadTable { get; } = [];
        public Dictionary<UnityContainerAssetInfo, string> AssetMap { get; } = [];

        // Skip preload table parsing; unnecessary for container-level path lookups
        public void FromAssetBundle(AssetsManager am, AssetsFileInstance fromFile, AssetTypeValueField assetBundleBf, bool loadPreloadTable = false)
        {
            if (loadPreloadTable)
            {
                AssetTypeValueField m_PreloadTable = assetBundleBf["m_PreloadTable.Array"];
                foreach (AssetTypeValueField ptr in m_PreloadTable)
                {
                    AssetPPtr assetPPtr = AssetPPtr.FromField(ptr);
                    assetPPtr.SetFilePathFromFile(am, fromFile);
                    PreloadTable.Add(assetPPtr);
                }
            }

            AssetTypeValueField m_Container = assetBundleBf["m_Container.Array"];
            foreach (AssetTypeValueField container in m_Container)
            {
                string key = container["first"].AsString;
                AssetTypeValueField value = container["second"];

                UnityContainerAssetInfo assetInfo = UnityContainerAssetInfo.FromField(value);
                assetInfo.assetPtr.SetFilePathFromFile(am, fromFile);
                if (assetInfo.assetPtr.PathId != 0)
                {
                    AssetMap[assetInfo] = key;
                }
            }
        }

        public void FromResourceManager(AssetsManager am, AssetsFileInstance fromFile, AssetTypeValueField rsrcManBf)
        {
            AssetTypeValueField m_Container = rsrcManBf["m_Container.Array"];
            foreach (AssetTypeValueField container in m_Container)
            {
                string key = container["first"].AsString;
                AssetTypeValueField value = container["second"];

                AssetPPtr assetPPtr = AssetPPtr.FromField(value);
                assetPPtr.SetFilePathFromFile(am, fromFile);

                UnityContainerAssetInfo assetInfo = new UnityContainerAssetInfo(assetPPtr);
                if (assetPPtr.PathId != 0)
                {
                    AssetMap[assetInfo] = key;
                }
            }
        }

        public static bool TryGetBundleContainerBaseField(
            UnityAssetWorkspace workspace, AssetsFileInstance file,

            [MaybeNullWhen(false)] out AssetsFileInstance actualFile,
            [MaybeNullWhen(false)] out AssetTypeValueField baseField
        )
        {
            actualFile = null;
            baseField = null;

            List<AssetFileInfo> assetBundleInfos = file.file.GetAssetsOfType(AssetClassID.AssetBundle);
            if (assetBundleInfos.Count == 0)
            {
                return false;
            }

            AssetContainer? bundleCont = workspace.GetAssetContainer(file, 0, assetBundleInfos[0].PathId, false);
            if (bundleCont == null || bundleCont.BaseValueField == null)
            {
                return false;
            }

            actualFile = file;
            baseField = bundleCont.BaseValueField;
            return true;
        }

        public static bool TryGetRsrcManContainerBaseField(
            UnityAssetWorkspace workspace, AssetsFileInstance file,
            [MaybeNullWhen(false)] out AssetsFileInstance actualFile,
            [MaybeNullWhen(false)] out AssetTypeValueField baseField
        )
        {
            actualFile = null;
            baseField = null;

            string gameDir = PathUtils.GetAssetsFileDirectory(file);
            if (gameDir == null)
            {
                return false;
            }

            // todo: what about mainData?
            string ggmPath = Path.Combine(gameDir, "globalgamemanagers");
            if (!File.Exists(ggmPath))
            {
                return false;
            }

            AssetsFileInstance ggmInst;
            int ggmIndex = workspace.LoadedFiles.FindIndex(f => f.path == ggmPath);
            if (ggmIndex != -1)
            {
                ggmInst = workspace.LoadedFiles[ggmIndex];
            }
            else
            {
                ggmInst = workspace.am.LoadAssetsFile(ggmPath, false);
            }

            List<AssetFileInfo> resourceManagerInfos = ggmInst.file.GetAssetsOfType(AssetClassID.ResourceManager);
            if (resourceManagerInfos.Count == 0)
            {
                return false;
            }

            AssetContainer? rsrcManCont = workspace.GetAssetContainer(ggmInst, 0, resourceManagerInfos[0].PathId, false);
            if (rsrcManCont != null && rsrcManCont.BaseValueField != null)
            {
                actualFile = rsrcManCont.FileInstance;
                baseField = rsrcManCont.BaseValueField;
                return true;
            }
            else
            {
                // Resolve directly from AssetsManager if absent from LoadedFiles
                actualFile = ggmInst;
                baseField = workspace.am.GetBaseField(ggmInst, resourceManagerInfos[0]);
                return true;
            }
        }

        public string? GetContainerPath(AssetsFileInstance fileInst, long pathId)
        {
            return GetContainerPath(new AssetPPtr(fileInst.path, 0, pathId));
        }

        public string? GetContainerPath(AssetPPtr assetPPtr)
        {
            UnityContainerAssetInfo search = new UnityContainerAssetInfo(assetPPtr);
            if (AssetMap.TryGetValue(search, out string? path))
            {
                return path;
            }

            return null;
        }
    }

    public class UnityContainerAssetInfo
    {
        public int PreloadIndex;
        public int PreloadSize;
        public AssetPPtr assetPtr;
        public object Name;

        public UnityContainerAssetInfo(AssetPPtr asset)
        {
            PreloadIndex = -1;
            PreloadSize = -1;
            assetPtr = asset;
            Name = string.Empty;
        }

        public UnityContainerAssetInfo(int preloadIndex, int preloadSize, AssetPPtr asset)
        {
            PreloadIndex = preloadIndex;
            PreloadSize = preloadSize;
            assetPtr = asset;
            Name = string.Empty;
        }

        public static UnityContainerAssetInfo FromField(AssetTypeValueField field)
        {
            int preloadIndex = field["preloadIndex"].AsInt;
            int preloadSize = field["preloadSize"].AsInt;
            AssetPPtr asset = AssetPPtr.FromField(field["asset"]);
            return new UnityContainerAssetInfo(preloadIndex, preloadSize, asset);
        }

        public override bool Equals(object? obj)
        {
            if (obj is not UnityContainerAssetInfo assetInfo)
                return false;

            return assetInfo.assetPtr.Equals(assetPtr);
        }

        public override int GetHashCode()
        {
            return assetPtr.GetHashCode();
        }
    }
}