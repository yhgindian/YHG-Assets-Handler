using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PluginAbstractions;

namespace AssetsHandler.Plugin.Unity
{
    public sealed partial class FileTypeDetector : IFileTypeDetector
    {

        /// <summary>
        /// Detects Unity format from a file path; wrapper around stream-based detection.
        /// </summary>
        public FormatKey DetectFileType(string filePath, long startAddress = 0)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return FormatKey.Unknown;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return Detect(fs, startAddress).Format;
            }
            catch
            {
                return FormatKey.Unknown;
            }
        }

        /// <summary>
        /// Detects UnityFS, UnityWeb, or serialized Unity .assets directly from a stream.
        /// </summary>
        public DetectionResult Detect(Stream stream, long startAddress = 0)
        {
            if (stream is null || !stream.CanRead || startAddress < 0)
                return DetectionResult.None;

            bool canSeek = stream.CanSeek;
            long originalPos = canSeek ? stream.Position : 0;

            try
            {
                if (canSeek)
                {
                    if (stream.Length <= startAddress || stream.Length - startAddress < 0x20)
                        return DetectionResult.None;

                    stream.Position = startAddress;
                }
                else if (startAddress > 0)
                {
                    byte[] skipBuf = new byte[8192];
                    long remaining = startAddress;
                    while (remaining > 0)
                    {
                        int toRead = (int)Math.Min(remaining, skipBuf.Length);
                        int read = stream.Read(skipBuf, 0, toRead);
                        if (read <= 0) return DetectionResult.None;
                        remaining -= read;
                    }
                }

                int checkSize = canSeek
                    ? (int)Math.Min(stream.Length - startAddress, 512)
                    : 512;

                byte[] headerBuffer = new byte[checkSize];
                int bytesRead = 0;

                while (bytesRead < checkSize)
                {
                    int read = stream.Read(headerBuffer, bytesRead, checkSize - bytesRead);
                    if (read <= 0) break;
                    bytesRead += read;
                }

                if (bytesRead < 0x14)
                    return DetectionResult.None;

                if (bytesRead >= 8)
                {
                    // UnityFS = 7 chars, UnityWeb = 8 chars (the old 7-char compare could never match UnityWeb)
                    if (Encoding.ASCII.GetString(headerBuffer, 0, 7) == "UnityFS")
                        return new DetectionResult(UnityFormats.Bundle, 100);

                    if (Encoding.ASCII.GetString(headerBuffer, 0, 8) == "UnityWeb")
                        return new DetectionResult(UnityFormats.Web, 100);
                }

                int possibleFormat = (headerBuffer[8] << 24) |
                                     (headerBuffer[9] << 16) |
                                     (headerBuffer[10] << 8) |
                                     headerBuffer[11];

                int versionOffset = possibleFormat >= 0x16 ? 0x30 : 0x14;

                if (bytesRead > versionOffset + 5)
                {
                    var sb = new StringBuilder();
                    for (int i = versionOffset; i < bytesRead; i++)
                    {
                        byte b = headerBuffer[i];
                        if (b == 0x00) break;
                        sb.Append((char)b);
                        if (sb.Length > 0xFF) break;
                    }

                    string possibleVersion = sb.ToString();
                    string emptyVersion = InvalidVersionCharsRegex().Replace(possibleVersion, string.Empty);
                    string fullVersion = ValidVersionCharsRegex().Replace(possibleVersion, string.Empty);

                    if (possibleFormat < 0xFF && emptyVersion.Length == 0 && fullVersion.Length >= 5)
                        return new DetectionResult(UnityFormats.Asset, 80);
                }
            }
            catch
            {
                return DetectionResult.None;
            }
            finally
            {
                if (canSeek)
                {
                    try { stream.Position = originalPos; }
                    catch { }
                }
            }

            return DetectionResult.None;
        }

        [GeneratedRegex("[a-zA-Z0-9\\.\\n\\-]")]
        private static partial Regex InvalidVersionCharsRegex();

        [GeneratedRegex("[^a-zA-Z0-9\\.\\n\\-]")]
        private static partial Regex ValidVersionCharsRegex();
    }
}