using AssetsTools.NET;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public class MinMaxAABB
    {
        public Vector3 m_Min;
        public Vector3 m_Max;

        public MinMaxAABB() { }

        public MinMaxAABB(AssetTypeValueField field)
        {
            if (field.IsDummy) return;

            var minField = field["m_Min"];
            if (!minField.IsDummy)
            {
                m_Min = new Vector3(
                    minField["x"]?.AsFloat ?? 0f,
                    minField["y"]?.AsFloat ?? 0f,
                    minField["z"]?.AsFloat ?? 0f);
            }

            var maxField = field["m_Max"];
            if (!maxField.IsDummy)
            {
                m_Max = new Vector3(
                    maxField["x"]?.AsFloat ?? 0f,
                    maxField["y"]?.AsFloat ?? 0f,
                    maxField["z"]?.AsFloat ?? 0f);
            }
        }

        public void WriteTo(AssetTypeValueField field)
        {
            if (field.IsDummy) return;

            var minField = field["m_Min"];
            if (!minField.IsDummy)
            {
                var f = minField["x"]; if (!f.IsDummy) f.AsFloat = m_Min.X;
                f = minField["y"]; if (!f.IsDummy) f.AsFloat = m_Min.Y;
                f = minField["z"]; if (!f.IsDummy) f.AsFloat = m_Min.Z;
            }

            var maxField = field["m_Max"];
            if (!maxField.IsDummy)
            {
                var f = maxField["x"]; if (!f.IsDummy) f.AsFloat = m_Max.X;
                f = maxField["y"]; if (!f.IsDummy) f.AsFloat = m_Max.Y;
                f = maxField["z"]; if (!f.IsDummy) f.AsFloat = m_Max.Z;
            }
        }
    }
}
