using AssetsTools.NET;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public class AABB
    {
        public Vector3 m_Center;
        public Vector3 m_Extent;

        public AABB() { }

        public AABB(AssetTypeValueField aabbField)
        {
            if (aabbField.IsDummy) return;

            var c = aabbField["m_Center"];
            if (!c.IsDummy)
            {
                m_Center = new Vector3(
                    c["x"]?.AsFloat ?? 0f,
                    c["y"]?.AsFloat ?? 0f,
                    c["z"]?.AsFloat ?? 0f);
            }

            var e = aabbField["m_Extent"];
            if (!e.IsDummy)
            {
                m_Extent = new Vector3(
                    e["x"]?.AsFloat ?? 0f,
                    e["y"]?.AsFloat ?? 0f,
                    e["z"]?.AsFloat ?? 0f);
            }
        }

        public void WriteTo(AssetTypeValueField aabbField)
        {
            if (aabbField.IsDummy) return;

            var c = aabbField["m_Center"];
            if (!c.IsDummy)
            {
                var f = c["x"]; if (!f.IsDummy) f.AsFloat = m_Center.X;
                f = c["y"]; if (!f.IsDummy) f.AsFloat = m_Center.Y;
                f = c["z"]; if (!f.IsDummy) f.AsFloat = m_Center.Z;
            }

            var e = aabbField["m_Extent"];
            if (!e.IsDummy)
            {
                var f = e["x"]; if (!f.IsDummy) f.AsFloat = m_Extent.X;
                f = e["y"]; if (!f.IsDummy) f.AsFloat = m_Extent.Y;
                f = e["z"]; if (!f.IsDummy) f.AsFloat = m_Extent.Z;
            }
        }
    }
}
