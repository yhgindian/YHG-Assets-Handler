using AssetsTools.NET;
using System;
using System.Collections.Generic;
using System.Text;

namespace AssetsHandler.Plugin.Unity.Workspace.UnityAssets.Classes
{
    public abstract class Component
    {
        public PPtr m_GameObject;

        protected Component(AssetTypeValueField field)
        {
            m_GameObject = new PPtr(field);
        }
    }
}
