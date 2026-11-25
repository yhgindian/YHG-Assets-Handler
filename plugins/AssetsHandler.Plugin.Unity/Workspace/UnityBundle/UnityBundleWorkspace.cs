using AssetsHandler.Core.Workspace;
using AssetsTools.NET.Extra;
using System;
using AssetsHandler.Plugin.Unity.Utils.Unity;

namespace AssetsHandler.Plugin.Unity.Workspace.UnityBundle
{
    public class UnityBundleWorkspace : BaseWorkspace
    {
        public AssetsManager am { get; }
        public BundleFileInstance? BundleInst { get; set; }

        public UnityBundleWorkspace() : this(AssetManagerUtils.Init()) { }

        public UnityBundleWorkspace(AssetsManager am, BundleFileInstance? bundleInst = null)
        {
            this.am = am ?? throw new ArgumentNullException(nameof(am));
            BundleInst = bundleInst;
        }

        protected override void OnDispose()
        {
            try
            {
                if (BundleInst != null)
                {
                    BundleInst.file?.Close();
                    BundleInst = null;
                }
                am?.UnloadAll();
            }
            catch
            {
                // Safe unload
            }
        }
    }
}