using AssetsTools.NET;
using System;
using System.Collections.Generic;
using System.Text;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public class ChannelInfo
    {
        public byte stream;
        public byte offset;
        public byte format;
        public byte dimension;

        public ChannelInfo() { }

        public ChannelInfo(AssetTypeValueField chanField)
        {
            if (chanField.IsDummy) return;

            var f = chanField["stream"];
            if (!f.IsDummy) stream = (byte)f.AsInt;

            f = chanField["offset"];
            if (!f.IsDummy) offset = (byte)f.AsInt;

            f = chanField["format"];
            if (!f.IsDummy) format = (byte)f.AsInt;

            f = chanField["dimension"];
            if (!f.IsDummy) dimension = (byte)(f.AsInt & 0xF);
        }

        public void WriteTo(AssetTypeValueField chanField)
        {
            if (chanField.IsDummy) return;

            var f = chanField["stream"];
            if (!f.IsDummy) f.AsInt = stream;

            f = chanField["offset"];
            if (!f.IsDummy) f.AsInt = offset;

            f = chanField["format"];
            if (!f.IsDummy) f.AsInt = format;

            f = chanField["dimension"];
            if (!f.IsDummy) f.AsInt = dimension & 0xF;
        }
    }
}
