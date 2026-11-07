using AssetsHandler.Core;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Fmod5Sharp;
using Fmod5Sharp.FmodTypes;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.AudioClip
{
    public sealed class AudioClipExportPlugin : IInternalAssetPlugin
    {
        public string DisplayName => "Export AudioClip";
        public int TargetClassId => (int)AssetClassID.AudioClip; // 83
        public PluginTargetMode TargetMode => PluginTargetMode.Any;

        public async Task ExecuteAsync(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets)
        {
            if (selectedAssets == null || selectedAssets.Count == 0) return;

            var validAssets = new List<UnityAssetObjectModel>();
            for (int i = 0; i < selectedAssets.Count; i++)
            {
                var model = selectedAssets[i];
                if (model.TypeClass == AssetClassID.AudioClip)
                {
                    validAssets.Add(model);
                }
            }

            if (validAssets.Count == 0)
            {
                NotificationManager.Warning("Selected items are not AudioClip assets.", "Export AudioClip");
                return;
            }

            if (validAssets.Count == 1)
            {
                await SingleExportAsync(workspace, validAssets[0]);
            }
            else
            {
                await BatchExportAsync(workspace, validAssets);
            }
        }

        #region Single Export
        private static async Task SingleExportAsync(UnityAssetWorkspace workspace, UnityAssetObjectModel assetModel)
        {
            var cont = assetModel.assetContainer;
            if (cont?.FileInstance == null) return;

            if (!cont.HasValueField)
            {
                var fullCont = workspace.GetAssetContainer(cont.FileInstance, 0, cont.PathId);
                if (fullCont == null) return;
                cont = fullCont;
            }

            if (cont.BaseValueField is not { } baseField)
            {
                NotificationManager.Error("Failed to read AudioClip base fields.", assetModel.Name);
                return;
            }

            var compressionFormat = (UnityAudioCompressionFormat)baseField["m_CompressionFormat"].AsInt;
            string ext = GetAudioExtension(compressionFormat);

            var sfd = new SaveFileDialog
            {
                Title = "Export AudioClip",
                FileName = $"{AssetNameUtils.getSaveFileName(assetModel)}.{ext}",
                Filter = $"{ext.ToUpper()} Audio File (*.{ext})|*.{ext}|All Files (*.*)|*.*",
                DefaultExt = ext
            };

            if (sfd.ShowDialog() != true) return;
            string outputPath = sfd.FileName;

            bool ok = await ProcessManager.RunAsync("Exporting AudioClip", async h =>
            {
                await Task.Run(() =>
                {
                    h.Token.ThrowIfCancellationRequested();
                    h.Report(0, $"Decoding {assetModel.Name} via Fmod5Sharp...");

                    byte[]? audioBytes = ExtractAudioClipBytes(cont, baseField);
                    if (audioBytes == null || audioBytes.Length == 0)
                    {
                        throw new InvalidOperationException("Failed to extract audio binary from archive or stream.");
                    }

                    File.WriteAllBytes(outputPath, audioBytes);
                    h.Report(100, "Completed");
                }, h.Token);
            });

            if (ok)
            {
                NotificationManager.Success(assetModel.Name, "AudioClip exported successfully");
            }
        }
        #endregion

        #region Batch Export
        private static async Task BatchExportAsync(UnityAssetWorkspace workspace, List<UnityAssetObjectModel> assets)
        {
            var ofd = new OpenFolderDialog
            {
                Title = "Select Destination Folder for AudioClips"
            };

            if (ofd.ShowDialog() != true) return;
            string targetFolder = ofd.FolderName;

            int total = assets.Count;
            int successCount = 0;
            var errorBuilder = new StringBuilder();

            await ProcessManager.RunAsync("Batch Exporting AudioClips", async h =>
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        if (h.Token.IsCancellationRequested) break;

                        var model = assets[i];
                        var cont = model.assetContainer;
                        h.Report((i + 1) * 100.0 / total, $"Exporting ({i + 1}/{total}): {model.Name}");

                        if (cont?.FileInstance == null) continue;

                        try
                        {
                            if (!cont.HasValueField)
                            {
                                var fullCont = workspace.GetAssetContainer(cont.FileInstance, 0, cont.PathId);
                                if (fullCont != null) cont = fullCont;
                            }

                            if (cont.BaseValueField is not { } baseField)
                            {
                                errorBuilder.AppendLine($"[{model.Name}]: Failed to read base field");
                                continue;
                            }

                            byte[]? audioBytes = ExtractAudioClipBytes(cont, baseField);
                            if (audioBytes == null || audioBytes.Length == 0)
                            {
                                errorBuilder.AppendLine($"[{model.Name}]: Failed to decode audio data");
                                continue;
                            }

                            var compressionFormat = (UnityAudioCompressionFormat)baseField["m_CompressionFormat"].AsInt;
                            string ext = GetAudioExtension(compressionFormat);
                            string fileName = $"{AssetNameUtils.getSaveFileName(model)}.{ext}";
                            string fullPath = Path.Combine(targetFolder, fileName);

                            File.WriteAllBytes(fullPath, audioBytes);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            errorBuilder.AppendLine($"{model.Name}: {ex.Message}");
                        }
                    }

                    h.Report(100, $"Finished: {successCount}/{total} exported.");
                }, h.Token);
            });

            if (errorBuilder.Length > 0)
            {
                Status.Write(errorBuilder.ToString(), StatusType.Warning);
            }

            NotificationManager.Success($"Exported {successCount}/{total} AudioClips.", "Batch Export Finished");
        }
        #endregion

        #region Extraction & Decoding Logic
        private static byte[]? ExtractAudioClipBytes(AssetContainer cont, AssetTypeValueField baseField)
        {
            string resourceSource = baseField["m_Resource.m_Source"].AsString;
            ulong resourceOffset = baseField["m_Resource.m_Offset"].AsULong;
            ulong resourceSize = baseField["m_Resource.m_Size"].AsULong;
            if (!GetRawFsbData(cont, resourceSource, resourceOffset, resourceSize, out byte[] fsbData))
            {
                return null;
            }
            if (!FsbLoader.TryLoadFsbFromByteArray(fsbData, out FmodSoundBank? bank) || bank == null || bank.Samples.Count == 0)
            {
                return null;
            }
            var sample = bank.Samples[0];
            sample.RebuildAsStandardFileFormat(out byte[]? sampleData, out string? sampleExtension);
            if (sampleData == null)
            {
                return null;
            }
            if (string.Equals(sampleExtension, "wav", StringComparison.OrdinalIgnoreCase))
            {
                FixWAV(ref sampleData);
            }
            return sampleData;
        }

        private static bool GetRawFsbData(AssetContainer cont, string filepath, ulong offset, ulong size, out byte[] audioData)
        {
            audioData = Array.Empty<byte>();
            if (string.IsNullOrEmpty(filepath))
            {
                return false;
            }
            var fileInst = cont.FileInstance;
            if (fileInst.parentBundle != null)
            {
                string searchPath = filepath;
                if (searchPath.StartsWith("archive:/", StringComparison.OrdinalIgnoreCase))
                    searchPath = searchPath.Substring(9);
                searchPath = Path.GetFileName(searchPath);
                var bundle = fileInst.parentBundle.file;
                var reader = bundle.DataReader;
                var dirInfos = bundle.BlockAndDirInfo.DirectoryInfos;
                for (int i = 0; i < dirInfos.Count; i++)
                {
                    var info = dirInfos[i];
                    if (string.Equals(info.Name, searchPath, StringComparison.OrdinalIgnoreCase))
                    {
                        lock (bundle.DataReader)
                        {
                            reader.Position = info.Offset + (long)offset;
                            audioData = reader.ReadBytes((int)size);
                        }
                        return audioData.Length > 0;
                    }
                }
            }
            string assetsDir = Path.GetDirectoryName(fileInst.path) ?? string.Empty;
            if (fileInst.parentBundle != null)
            {
                assetsDir = Path.GetDirectoryName(assetsDir) ?? assetsDir;
            }
            string directPath = Path.Combine(assetsDir, filepath);
            if (File.Exists(directPath))
            {
                using var fs = new FileStream(directPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Position = (long)offset;
                audioData = new byte[size];
                fs.ReadExactly(audioData, 0, (int)size);
                return true;
            }
            string fileNameOnly = Path.Combine(assetsDir, Path.GetFileName(filepath));
            if (File.Exists(fileNameOnly))
            {
                using var fs = new FileStream(fileNameOnly, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Position = (long)offset;
                audioData = new byte[size];
                fs.ReadExactly(audioData, 0, (int)size);
                return true;
            }
            return false;
        }

        private static void FixWAV(ref byte[] wavData)
        {
            int origLength = wavData.Length;
            if (origLength < 44) return;
            for (int i = 36; i < origLength - 2; i++)
            {
                wavData[i] = wavData[i + 2];
            }
            Array.Resize(ref wavData, origLength - 2);
            byte[] riffChunkSize = BitConverter.GetBytes(wavData.Length - 8);
            if (!BitConverter.IsLittleEndian) Array.Reverse(riffChunkSize);
            riffChunkSize.CopyTo(wavData, 4);
            byte[] fmtChunkSize = BitConverter.GetBytes(16);
            if (!BitConverter.IsLittleEndian) Array.Reverse(fmtChunkSize);
            fmtChunkSize.CopyTo(wavData, 16);
            byte[] dataChunkSize = BitConverter.GetBytes(wavData.Length - 44);
            if (!BitConverter.IsLittleEndian) Array.Reverse(dataChunkSize);
            dataChunkSize.CopyTo(wavData, 40);
        }

        private static string GetAudioExtension(UnityAudioCompressionFormat format)
        {
            return format switch
            {
                UnityAudioCompressionFormat.PCM => "wav",
                UnityAudioCompressionFormat.Vorbis => "ogg",
                UnityAudioCompressionFormat.ADPCM => "wav",
                UnityAudioCompressionFormat.MP3 => "mp3",
                UnityAudioCompressionFormat.AAC => "aac",
                UnityAudioCompressionFormat.GCADPCM => "wav",
                _ => "dat"
            };
        }
        #endregion
    }
}