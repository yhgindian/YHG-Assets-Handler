namespace PluginAbstractions;

public readonly record struct DetectionResult(FormatKey Format, int Confidence)
{
    public static readonly DetectionResult None = new(FormatKey.Unknown, 0);
    public bool IsMatch => Format != FormatKey.Unknown && Confidence > 0;
}

public interface IFileTypeDetector
{
    /// <summary>
    /// Inspect the stream starting at <paramref name="startAddress"/>. Must restore nothing:
    /// the host passes a fresh seekable stream and rewinds it between detectors.
    /// Confidence is 0..100; 100 = certain (stops further detection).
    /// </summary>
    DetectionResult Detect(Stream stream, long startAddress = 0);
}
