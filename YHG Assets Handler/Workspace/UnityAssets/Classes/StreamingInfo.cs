using AssetsTools.NET;
using System;
using System.Collections.Generic;
using System.Text;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes
{
    public class StreamingInfo
    {
        public ulong offset;
        public uint size;
        public string path = string.Empty;

        public StreamingInfo() { }

        public StreamingInfo(AssetTypeValueField field)
        {
            if (field.IsDummy) return;

            var f = field["offset"];
            if (!f.IsDummy) offset = f.AsULong;

            f = field["size"];
            if (!f.IsDummy) size = f.AsUInt;

            f = field["path"];
            if (!f.IsDummy) path = f.AsString;
        }

        public void WriteTo(AssetTypeValueField field)
        {
            if (field.IsDummy) return;

            var f = field["offset"];
            if (!f.IsDummy) f.AsULong = offset;

            f = field["size"];
            if (!f.IsDummy) f.AsUInt = size;

            f = field["path"];
            if (!f.IsDummy) f.AsString = path;
        }
    }
}
