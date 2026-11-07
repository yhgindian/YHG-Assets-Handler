using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public class MeshBlendShape
    {
        public string name = string.Empty;
        public uint firstVertex;
        public uint vertexCount;
        public Vector3 aabbMinDelta;
        public Vector3 aabbMaxDelta;
        public bool hasNormals;
        public bool hasTangents;

        public MeshBlendShape() { }

        public MeshBlendShape(AssetTypeValueField field, UnityVersion version)
        {
            if (field.IsDummy) return;

            if (version.major == 4 && version.minor < 3)
            {
                var n = field["name"];
                if (!n.IsDummy) name = n.AsString;

                var minD = field["aabbMinDelta"];
                if (!minD.IsDummy)
                    aabbMinDelta = new Vector3(minD["x"]?.AsFloat ?? 0f, minD["y"]?.AsFloat ?? 0f, minD["z"]?.AsFloat ?? 0f);

                var maxD = field["aabbMaxDelta"];
                if (!maxD.IsDummy)
                    aabbMaxDelta = new Vector3(maxD["x"]?.AsFloat ?? 0f, maxD["y"]?.AsFloat ?? 0f, maxD["z"]?.AsFloat ?? 0f);
            }

            var f = field["firstVertex"];
            if (!f.IsDummy) firstVertex = f.AsUInt;

            f = field["vertexCount"];
            if (!f.IsDummy) vertexCount = f.AsUInt;

            f = field["hasNormals"];
            if (!f.IsDummy) hasNormals = f.AsBool;

            f = field["hasTangents"];
            if (!f.IsDummy) hasTangents = f.AsBool;
        }

        public void WriteTo(AssetTypeValueField field, UnityVersion version)
        {
            if (field.IsDummy) return;

            if (version.major == 4 && version.minor < 3)
            {
                var n = field["name"];
                if (!n.IsDummy) n.AsString = name;

                var minD = field["aabbMinDelta"];
                if (!minD.IsDummy)
                {
                    var fVal = minD["x"]; if (!fVal.IsDummy) fVal.AsFloat = aabbMinDelta.X;
                    fVal = minD["y"]; if (!fVal.IsDummy) fVal.AsFloat = aabbMinDelta.Y;
                    fVal = minD["z"]; if (!fVal.IsDummy) fVal.AsFloat = aabbMinDelta.Z;
                }

                var maxD = field["aabbMaxDelta"];
                if (!maxD.IsDummy)
                {
                    var fVal = maxD["x"]; if (!fVal.IsDummy) fVal.AsFloat = aabbMaxDelta.X;
                    fVal = maxD["y"]; if (!fVal.IsDummy) fVal.AsFloat = aabbMaxDelta.Y;
                    fVal = maxD["z"]; if (!fVal.IsDummy) fVal.AsFloat = aabbMaxDelta.Z;
                }
            }

            var f = field["firstVertex"];
            if (!f.IsDummy) f.AsUInt = firstVertex;

            f = field["vertexCount"];
            if (!f.IsDummy) f.AsUInt = vertexCount;

            f = field["hasNormals"];
            if (!f.IsDummy) f.AsBool = hasNormals;

            f = field["hasTangents"];
            if (!f.IsDummy) f.AsBool = hasTangents;
        }
    }
}
