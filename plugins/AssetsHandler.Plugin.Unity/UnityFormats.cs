using System.IO;

namespace AssetsHandler.Plugin.Unity
{
    /// <summary>Format keys owned by this plugin plus small detection helpers for internal use.</summary>
    public static class UnityFormats
    {
        public static readonly FormatKey Asset = new("unity.asset");
        public static readonly FormatKey Bundle = new("unity.bundle");
        public static readonly FormatKey Web = new("unity.web");

        public static readonly FileTypeDetector Detector = new();

        public static FormatKey Detect(string path) => Detector.DetectFileType(path);
        public static FormatKey Detect(Stream stream) => Detector.Detect(stream).Format;
    }
}
