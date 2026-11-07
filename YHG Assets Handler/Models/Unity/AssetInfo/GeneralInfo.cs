using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.Text;
using YHG_Assets_Handler.Utils.Unity;

namespace YHG_Assets_Handler.Models.Unity.AssetInfo
{
    public class GeneralInfo
    {
        public string MetadataSize { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string FirstFileOffset { get; set; } = string.Empty;
        public string Endianness { get; set; } = string.Empty;
        public string EngineVersion { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string TypeTreeEnabled { get; set; } = string.Empty;

        public GeneralInfo(AssetsFileInstance file)
        {
            var header = file.file.Header;
            var metadata = file.file.Metadata;

            MetadataSize = $"{header.MetadataSize:N0} bytes";
            FileSize = $"{header.FileSize:N0} bytes";
            Format = header.Version.ToString();
            FirstFileOffset = $"0x{header.DataOffset:X8} ({header.DataOffset})";
            Endianness = header.Endianness ? "Big endian" : "Little endian";

            EngineVersion = string.IsNullOrEmpty(metadata.UnityVersion) ? "Unknown" : metadata.UnityVersion;
            Platform = $"{(BuildTarget)metadata.TargetPlatform} ({metadata.TargetPlatform})";
            TypeTreeEnabled = metadata.TypeTreeEnabled ? "Enabled" : "Disabled";
        }

        public GeneralInfo() { }

        public static GeneralInfo Empty { get; } = new();
    }
}
