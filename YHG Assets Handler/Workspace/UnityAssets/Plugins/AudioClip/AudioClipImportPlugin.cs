using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.ViewModels.Unity;
using YHG_Assets_Handler.Views;
using YHG_Assets_Handler.Views.Windows.Unity;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.AudioClip
{
    public class AudioClipImportPlugin : IInternalAssetPlugin
    {
        public string DisplayName => "Import Audio...";
        public int TargetClassId => (int)AssetClassID.AudioClip;
        public PluginTargetMode TargetMode => PluginTargetMode.Any;

        public async Task ExecuteAsync(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets)
        {
            if (selectedAssets == null || selectedAssets.Count == 0)
            {
                NotificationManager.Warning("Please select at least one AudioClip to import.", "Audio Import");
                return;
            }

            var validContainers = selectedAssets
                .Select(x => x.assetContainer)
                .OfType<AssetContainer>()
                .ToList();

            if (validContainers.Count == 0)
            {
                NotificationManager.Warning("No valid asset containers found in selection.", "Audio Import");
                return;
            }

            // ───────────── CASE 1: Single Asset Import ─────────────
            if (validContainers.Count == 1)
            {
                var cont = validContainers[0];

                var ofd = new OpenFileDialog
                {
                    Title = "Select Audio File to Import",
                    Filter = "All Supported Audio (*.wav;*.ogg;*.mp3;*.aac;*.dat)|*.wav;*.ogg;*.mp3;*.aac;*.dat|WAV (*.wav)|*.wav|OGG (*.ogg)|*.ogg|MP3 (*.mp3)|*.mp3|AAC (*.aac)|*.aac|All Files (*.*)|*.*",
                    Multiselect = false
                };

                if (ofd.ShowDialog() != true)
                    return;

                string filePath = ofd.FileName;

                bool success = await ProcessManager.RunAsync("Importing Audio Clip", async h =>
                {
                    await Task.Run(() =>
                    {
                        h.Token.ThrowIfCancellationRequested();
                        h.SetIndeterminate($"Processing {Path.GetFileName(filePath)}...");
                        ApplyAudioToContainer(cont, workspace, filePath);
                    }, h.Token);
                });

                if (success)
                {
                    NotificationManager.Success("Audio clip updated successfully!", Path.GetFileName(filePath));
                }
                return;
            }

            // ───────────── CASE 2: Batch Asset Import (Using BatchImportView) ─────────────
            var dirDlg = new OpenFolderDialog
            {
                Title = "Select Directory Containing Audio Files"
            };

            if (dirDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dirDlg.FolderName))
                return;

            string directory = dirDlg.FolderName;
            var extensions = new List<string> { "wav", "mp3", "ogg", "aac", "dat" };

            var vm = new BatchImportViewModel(workspace, validContainers, directory, extensions);
            var view = new BatchImportView(vm)
            {
                Owner = Application.Current.MainWindow
            };

            if (view.ShowDialog() != true)
                return;

            if (vm.ConfirmedImports.Count == 0)
            {
                NotificationManager.Info("No matching audio files found for the selected assets.", "Import Audio");
                return;
            }

            var importTargets = vm.ConfirmedImports.ToList();
            int total = importTargets.Count;
            int imported = 0, failed = 0;

            bool completed = await ProcessManager.RunAsync("Batch Importing Audio Clips", async h =>
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        h.Token.ThrowIfCancellationRequested();

                        var batchInfo = importTargets[i];
                        h.Report((i + 1) * 100.0 / total, $"Importing ({i + 1}/{total}): {batchInfo.AssetName}");

                        try
                        {
                            if (batchInfo.Container != null)
                            {
                                ApplyAudioToContainer(batchInfo.Container, workspace, batchInfo.ImportFilePath);
                                imported++;
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            Status.Write($"Audio import failed for {batchInfo.AssetName}: {ex.Message}", StatusType.Error);
                        }
                    }

                    h.Report(100, $"Finished: {imported}/{total} clips imported.");
                }, h.Token);
            });

            if (!completed)
            {
                NotificationManager.Warning($"Stopped after {imported}/{total} audio clips.", "Batch Audio Import");
            }
            else if (failed == 0)
            {
                NotificationManager.Success($"Imported {imported}/{total} audio clips.", "Batch Audio Import");
            }
            else
            {
                NotificationManager.Warning($"{imported} imported, {failed} failed. Check the console.", "Batch Audio Import");
            }
        }

        private static void ApplyAudioToContainer(AssetContainer cont, UnityAssetWorkspace workspace, string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Audio file does not exist.", filePath);

            if (cont.FileInstance == null || cont.FileInstance.file == null)
                throw new InvalidOperationException("Asset container file instance is missing.");

            using var audioFile = TagLib.File.Create(filePath);
            var props = audioFile.Properties;

            int channels = props.AudioChannels;
            int frequency = props.AudioSampleRate;
            int bitsPerSample = props.BitsPerSample > 0 ? props.BitsPerSample : 16;
            float lengthSec = (float)props.Duration.TotalSeconds;

            var baseField = workspace.GetAssetContainer(cont.FileInstance, 0, cont.PathId)?.BaseValueField;
            if (baseField == null)
            {
                var tempField = workspace.GetTemplateField(cont);
                baseField = tempField.MakeValue(cont.FileReader, cont.AbsoluteByteStart);
                cont.BaseValueField = baseField;
            }

            byte[] audioRawBytes = File.ReadAllBytes(filePath);

            var resField = baseField["m_Resource"];
            string existingSource = resField?["m_Source"]?.AsString ?? string.Empty;

            if (resField != null && !string.IsNullOrEmpty(existingSource))
            {
                string dir = Path.GetDirectoryName(cont.FileInstance.path)!;
                string resPath = Path.Combine(dir, existingSource);

                if (File.Exists(resPath))
                {
                    using var fs = new FileStream(resPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                    long appendOffset = fs.Length;
                    fs.Seek(appendOffset, SeekOrigin.Begin);
                    fs.Write(audioRawBytes, 0, audioRawBytes.Length);

                    if (resField["m_Offset"] != null)
                        resField["m_Offset"].AsULong = (ulong)appendOffset;

                    if (resField["m_Size"] != null)
                        resField["m_Size"].AsULong = (ulong)audioRawBytes.Length;
                }
                else
                {
                    
                    string targetCopy = Path.Combine(dir, Path.GetFileName(filePath));
                    if (!File.Exists(targetCopy))
                    {
                        File.Copy(filePath, targetCopy, overwrite: true);
                    }

                    resField["m_Source"].AsString = Path.GetFileName(targetCopy);

                    if (resField["m_Offset"] != null)
                        resField["m_Offset"].AsULong = 0;

                    if (resField["m_Size"] != null)
                        resField["m_Size"].AsULong = (ulong)audioRawBytes.Length;
                }
            }
            else if (baseField["m_AudioData"] != null)
            {
                baseField["m_AudioData"].AsByteArray = audioRawBytes;
            }

            // 4. Unity AudioClip properties safe assign
            SetFieldIfExists(baseField, "m_LoadType", 0); // Decompress on load
            SetFieldIfExists(baseField, "m_Channels", channels);
            SetFieldIfExists(baseField, "m_Frequency", frequency);
            SetFieldIfExists(baseField, "m_BitsPerSample", bitsPerSample);
            SetFieldIfExists(baseField, "m_Length", lengthSec);
            SetFieldIfExists(baseField, "m_IsTrackerFormat", false);
            SetFieldIfExists(baseField, "m_Ambisonic", false);
            SetFieldIfExists(baseField, "m_SubsoundIndex", 0);
            SetFieldIfExists(baseField, "m_PreloadAudioData", true);
            SetFieldIfExists(baseField, "m_LoadInBackground", false);
            SetFieldIfExists(baseField, "m_Legacy3D", false);
            SetFieldIfExists(baseField, "m_CompressionFormat", (int)DetermineCompressionFormat(filePath));

            byte[] serializedAsset = baseField.WriteToByteArray();
            workspace.AddOrUpdateAsset(cont, serializedAsset);
        }

        private static void SetFieldIfExists(AssetTypeValueField parent, string fieldName, object value)
        {
            var field = parent[fieldName];
            if (field == null || field.IsDummy) return;

            switch (value)
            {
                case int i: field.AsInt = i; break;
                case uint ui: field.AsUInt = ui; break;
                case long l: field.AsLong = l; break;
                case ulong ul: field.AsULong = ul; break;
                case float f: field.AsFloat = f; break;
                case bool b: field.AsBool = b; break;
                case string s: field.AsString = s; break;
            }
        }

        private static UnityAudioCompressionFormat DetermineCompressionFormat(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".wav" => UnityAudioCompressionFormat.PCM,
                ".ogg" => UnityAudioCompressionFormat.Vorbis,
                ".mp3" => UnityAudioCompressionFormat.MP3,
                ".aac" => UnityAudioCompressionFormat.AAC,
                ".dat" => UnityAudioCompressionFormat.VAG,
                _ => UnityAudioCompressionFormat.MP3
            };
        }
    }
}