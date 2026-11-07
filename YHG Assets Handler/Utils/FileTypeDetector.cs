using AssetsHandler.Core;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace YHG_Assets_Handler.Utils
{
    public static class FileTypeDetector
    {
        /// <summary>
        /// Detects the file format from a file path; wrapper around the stream-based detection overload.
        /// </summary>
        public static FileType DetectFileType(string filePath, long startAddress = 0)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return FileType.Unknown;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return DetectFileType(fs, startAddress);
            }
            catch
            {
                return FileType.Unknown;
            }
        }

        /// <summary>
        /// Detects the underlying file format directly from an active seekable stream.
        /// </summary>
        public static FileType DetectFileType(Stream stream, long startAddress = 0)
        {
            if (stream == null || !stream.CanRead)
                return FileType.Unknown;

            long originalPos = stream.Position;

            try
            {
                if (stream.Length <= startAddress)
                    return FileType.Unknown;

                if (stream.Length - startAddress >= 0x20)
                {
                    stream.Position = startAddress;
                    int checkSize = (int)Math.Min(stream.Length - startAddress, 512);
                    byte[] headerBuffer = new byte[checkSize];

                    int bytesRead = 0;
                    while (bytesRead < checkSize)
                    {
                        int read = stream.Read(headerBuffer, bytesRead, checkSize - bytesRead);
                        if (read <= 0) break;
                        bytesRead += read;
                    }

                    if (bytesRead < 0x14) return FileType.Unknown;

                    // 1. UnityFS / UnityWeb Bundle check
                    if (bytesRead >= 8)
                    {
                        string possibleBundleHeader = Encoding.ASCII.GetString(headerBuffer, 0, 7);
                        if (possibleBundleHeader == "UnityFS" || possibleBundleHeader.StartsWith("UnityWeb"))
                        {
                            return possibleBundleHeader.StartsWith("UnityWeb")
                                ? FileType.UnityWebFile
                                : FileType.UnityBundle;
                        }
                    }

                    // 2. Unity .assets file Big-Endian precision check
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
                        string emptyVersion = Regex.Replace(possibleVersion, "[a-zA-Z0-9\\.\\n\\-]", "");
                        string fullVersion = Regex.Replace(possibleVersion, "[^a-zA-Z0-9\\.\\n\\-]", "");

                        if (possibleFormat < 0xFF && emptyVersion.Length == 0 && fullVersion.Length >= 5)
                        {
                            return FileType.UnityAsset;
                        }
                    }
                }
            }
            catch
            {
                // Fallback safe
            }
            finally
            {
                if (stream.CanSeek)
                {
                    stream.Position = originalPos;
                }
            }

            return FileType.Unknown;
        }
    }
}