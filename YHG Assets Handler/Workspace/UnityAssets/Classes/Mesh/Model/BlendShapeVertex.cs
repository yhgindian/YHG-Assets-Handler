using AssetsTools.NET;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public class BlendShapeVertex
    {
        public Vector3 vertex;
        public Vector3 normal;
        public Vector3 tangent;
        public uint index;

        public BlendShapeVertex() { }

        public BlendShapeVertex(AssetTypeValueField field)
        {
            if (field.IsDummy) return;

            var v = field["vertex"];
            if (!v.IsDummy)
                vertex = new Vector3(v["x"]?.AsFloat ?? 0f, v["y"]?.AsFloat ?? 0f, v["z"]?.AsFloat ?? 0f);

            var n = field["normal"];
            if (!n.IsDummy)
                normal = new Vector3(n["x"]?.AsFloat ?? 0f, n["y"]?.AsFloat ?? 0f, n["z"]?.AsFloat ?? 0f);

            var t = field["tangent"];
            if (!t.IsDummy)
                tangent = new Vector3(t["x"]?.AsFloat ?? 0f, t["y"]?.AsFloat ?? 0f, t["z"]?.AsFloat ?? 0f);

            var idx = field["index"];
            if (!idx.IsDummy) index = idx.AsUInt;
        }

        public void WriteTo(AssetTypeValueField field)
        {
            if (field.IsDummy) return;

            var v = field["vertex"];
            if (!v.IsDummy)
            {
                var f = v["x"]; if (!f.IsDummy) f.AsFloat = vertex.X;
                f = v["y"]; if (!f.IsDummy) f.AsFloat = vertex.Y;
                f = v["z"]; if (!f.IsDummy) f.AsFloat = vertex.Z;
            }

            var n = field["normal"];
            if (!n.IsDummy)
            {
                var f = n["x"]; if (!f.IsDummy) f.AsFloat = normal.X;
                f = n["y"]; if (!f.IsDummy) f.AsFloat = normal.Y;
                f = n["z"]; if (!f.IsDummy) f.AsFloat = normal.Z;
            }

            var t = field["tangent"];
            if (!t.IsDummy)
            {
                var f = t["x"]; if (!f.IsDummy) f.AsFloat = tangent.X;
                f = t["y"]; if (!f.IsDummy) f.AsFloat = tangent.Y;
                f = t["z"]; if (!f.IsDummy) f.AsFloat = tangent.Z;
            }

            var idx = field["index"];
            if (!idx.IsDummy) idx.AsUInt = index;
        }
    }
}
