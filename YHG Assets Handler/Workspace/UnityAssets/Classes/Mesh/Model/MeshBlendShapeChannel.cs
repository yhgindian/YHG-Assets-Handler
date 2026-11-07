using AssetsTools.NET;
using System;
using System.Collections.Generic;
using System.Text;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public class MeshBlendShapeChannel
    {
        public string name = string.Empty;
        public uint nameHash;
        public int frameIndex;
        public int frameCount;

        public MeshBlendShapeChannel() { }

        public MeshBlendShapeChannel(AssetTypeValueField field)
        {
            if (field.IsDummy) return;

            var f = field["name"];
            if (!f.IsDummy) name = f.AsString;

            f = field["nameHash"];
            if (!f.IsDummy) nameHash = f.AsUInt;

            f = field["frameIndex"];
            if (!f.IsDummy) frameIndex = f.AsInt;

            f = field["frameCount"];
            if (!f.IsDummy) frameCount = f.AsInt;
        }

        public void WriteTo(AssetTypeValueField field)
        {
            if (field.IsDummy) return;

            var f = field["name"];
            if (!f.IsDummy) f.AsString = name;

            f = field["nameHash"];
            if (!f.IsDummy) f.AsUInt = nameHash;

            f = field["frameIndex"];
            if (!f.IsDummy) f.AsInt = frameIndex;

            f = field["frameCount"];
            if (!f.IsDummy) f.AsInt = frameCount;
        }
    }
}
