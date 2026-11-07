using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace YHG_Assets_Handler.Utils.Unity
{
    public static class AssetsFileMergeHelper
    {
        private const int BufferSize = 128 * 1024; // 128 KB buffer

        /// <summary>
        /// Discovers and sequentially orders all matching split archive parts based on an arbitrary part path.
        /// Resolves split patterns matching the naming convention: .split0, .split1, ... or zero-padded variants.
        /// </summary>
        /// <param name="anySplitFilePath">Absolute or relative file path to any valid split part.</param>
        /// <returns>An array of resolved split file paths sorted sequentially by index.</returns>
        public static string[] GetOrderedSplitFiles(string anySplitFilePath)
        {
            if (string.IsNullOrWhiteSpace(anySplitFilePath) || !File.Exists(anySplitFilePath))
                return Array.Empty<string>();

            string? directory = Path.GetDirectoryName(anySplitFilePath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return Array.Empty<string>();

            // Extract the base filename excluding the trailing .split{N} extension
            string fileName = Path.GetFileName(anySplitFilePath);
            var match = Regex.Match(fileName, @"^(.*)\.split\d+$", RegexOptions.IgnoreCase);

            string baseName = match.Success ? match.Groups[1].Value : Path.GetFileNameWithoutExtension(anySplitFilePath);
            string searchPattern = $"{baseName}.split*";

            return Directory.GetFiles(directory, searchPattern)
                .Where(f => Regex.IsMatch(Path.GetExtension(f), @"^\.split\d+$", RegexOptions.IgnoreCase))
                .OrderBy(f =>
                {
                    string ext = Path.GetExtension(f).ToLower().Replace(".split", "");
                    return int.TryParse(ext, out int index) ? index : int.MaxValue;
                })
                .ToArray();
        }

        /// <summary>
        /// Reassembles split chunk files into a single destination file asynchronously using buffered streams.
        /// Performs direct disk-to-disk streaming to minimize memory footprint.
        /// </summary>
        /// <param name="anySplitFilePath">Path to any part of the target split archive.</param>
        /// <param name="destinationFilePath">Path where the merged file will be written.</param>
        /// <param name="progress">Optional progress provider reporting completion percentage (0 - 100).</param>
        public static async Task MergeToFileAsync(string anySplitFilePath, string destinationFilePath, IProgress<double>? progress = null)
        {
            var splitFiles = GetOrderedSplitFiles(anySplitFilePath);
            if (splitFiles.Length == 0)
                throw new FileNotFoundException("No matching split parts found.");

            long totalBytes = splitFiles.Sum(f => new FileInfo(f).Length);
            long writtenBytes = 0;
            byte[] buffer = new byte[BufferSize];

            using (var outputStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true))
            {
                foreach (var splitFile in splitFiles)
                {
                    using (var inputStream = new FileStream(splitFile, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true))
                    {
                        int read;
                        while ((read = await inputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await outputStream.WriteAsync(buffer, 0, read);
                            writtenBytes += read;
                            if (totalBytes > 0)
                                progress?.Report((double)writtenBytes / totalBytes * 100.0);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Combines split files into an in-memory seekable stream without writing to disk.
        /// </summary>
        /// <param name="anySplitFilePath">Path to any part of the target split archive.</param>
        /// <returns>A seekable <see cref="MemoryStream"/> containing the merged binary payload, or null if no parts exist.</returns>
        public static MemoryStream? MergeToStream(string anySplitFilePath)
        {
            var splitFiles = GetOrderedSplitFiles(anySplitFilePath);
            if (splitFiles.Length == 0) return null;

            var ms = new MemoryStream();
            byte[] buffer = new byte[BufferSize];

            foreach (var file in splitFiles)
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int read;
                    while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ms.Write(buffer, 0, read);
                    }
                }
            }
            ms.Position = 0;
            return ms;
        }

        /// <summary>
        /// Reads and concatenates all split archive parts into a contiguous byte array.
        /// </summary>
        /// <param name="anySplitFilePath">Path to any part of the target split archive.</param>
        /// <returns>The combined byte array, or null if no parts exist.</returns>
        public static byte[]? MergeToBytes(string anySplitFilePath)
        {
            using (var stream = MergeToStream(anySplitFilePath))
            {
                return stream?.ToArray();
            }
        }
    }
}