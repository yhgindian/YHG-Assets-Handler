using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.Text;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public class StreamInfo
    {
        public uint channelMask;
        public uint offset;
        public uint stride;
        public uint align;
        public byte dividerOp;
        public ushort frequency;

        public StreamInfo() { }

        public StreamInfo(AssetTypeValueField streamField, UnityVersion version)
        {
            if (streamField.IsDummy) return;

            var f = streamField["channelMask"];
            if (!f.IsDummy) channelMask = f.AsUInt;

            f = streamField["offset"];
            if (!f.IsDummy) offset = f.AsUInt;

            if (version.major < 4)
            {
                f = streamField["stride"];
                if (!f.IsDummy) stride = f.AsUInt;

                f = streamField["align"];
                if (!f.IsDummy) align = f.AsUInt;
            }
            else
            {
                f = streamField["stride"];
                if (!f.IsDummy) stride = (uint)streamField["stride"].AsInt;

                f = streamField["dividerOp"];
                if (!f.IsDummy) dividerOp = (byte)streamField["dividerOp"].AsInt;

                f = streamField["frequency"];
                if (!f.IsDummy) frequency = (ushort)streamField["frequency"].AsInt;
            }
        }

        public void WriteTo(AssetTypeValueField streamField, UnityVersion version)
        {
            if (streamField.IsDummy) return;

            var f = streamField["channelMask"];
            if (!f.IsDummy) f.AsUInt = channelMask;

            f = streamField["offset"];
            if (!f.IsDummy) f.AsUInt = offset;

            if (version.major < 4)
            {
                f = streamField["stride"];
                if (!f.IsDummy) f.AsUInt = stride;

                f = streamField["align"];
                if (!f.IsDummy) f.AsUInt = align;
            }
            else
            {
                f = streamField["stride"];
                if (!f.IsDummy) f.AsInt = (int)stride;

                f = streamField["dividerOp"];
                if (!f.IsDummy) f.AsInt = dividerOp;

                f = streamField["frequency"];
                if (!f.IsDummy) f.AsInt = frequency;
            }
        }
    }
}
