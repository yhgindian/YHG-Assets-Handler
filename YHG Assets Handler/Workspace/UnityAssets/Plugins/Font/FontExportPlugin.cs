using AssetsHandler.Core;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Font
{
    public sealed class FontExportPlugin : IInternalAssetPlugin
    {
        public string DisplayName => "Export Font";
        public int TargetClassId => (int)AssetClassID.Font; // 128
        public PluginTargetMode TargetMode => PluginTargetMode.Any;

        public async Task ExecuteAsync(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets)
        {
            if (selectedAssets == null || selectedAssets.Count == 0) return;

            var validAssets = new List<UnityAssetObjectModel>();
            for (int i = 0; i < selectedAssets.Count; i++)
            {
                var model = selectedAssets[i];
                if (model.TypeClass == AssetClassID.Font)
                {
                    validAssets.Add(model);
                }
            }

            if (validAssets.Count == 0)
            {
                NotificationManager.Warning("Selected items are not Font assets.", "Export Font");
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

            var baseField = FontHelper.GetByteArrayFont(workspace, cont);
            if (baseField == null)
            {
                NotificationManager.Error("Failed to read Font asset template.", assetModel.Name);
                return;
            }

            var fontDataField = baseField["m_FontData"];
            var arrayField = fontDataField.IsDummy ? null : fontDataField["Array"];
            byte[]? fontBytes = arrayField?.AsByteArray;

            if (fontBytes == null || fontBytes.Length == 0)
            {
                NotificationManager.Warning("Font has no embedded vector data (m_FontData is empty).", assetModel.Name);
                return;
            }

            bool isOtf = FontHelper.IsDataOtf(fontBytes);
            string ext = isOtf ? "otf" : "ttf";
            string filterDesc = isOtf ? "OpenType Font (*.otf)|*.otf" : "TrueType Font (*.ttf)|*.ttf";

            var sfd = new SaveFileDialog
            {
                Title = "Export Font",
                FileName = $"{AssetNameUtils.getSaveFileName(assetModel)}.{ext}",
                Filter = $"{filterDesc}|All Files (*.*)|*.*",
                DefaultExt = ext
            };

            if (sfd.ShowDialog() != true) return;
            string outputPath = sfd.FileName;

            bool ok = await ProcessManager.RunAsync("Exporting Font", async h =>
            {
                await Task.Run(() =>
                {
                    h.Token.ThrowIfCancellationRequested();
                    h.Report(0, $"Writing {assetModel.Name}...");

                    File.WriteAllBytes(outputPath, fontBytes);
                    h.Report(100, "Completed");
                }, h.Token);
            });

            if (ok)
            {
                NotificationManager.Success(assetModel.Name, "Font exported successfully");
            }
        }
        #endregion

        #region Batch Export
        private static async Task BatchExportAsync(UnityAssetWorkspace workspace, List<UnityAssetObjectModel> assets)
        {
            var ofd = new OpenFolderDialog
            {
                Title = "Select Destination Folder for Fonts"
            };

            if (ofd.ShowDialog() != true) return;
            string targetFolder = ofd.FolderName;

            int total = assets.Count;
            int successCount = 0;
            int emptyCount = 0;
            var errorBuilder = new StringBuilder();

            await ProcessManager.RunAsync("Batch Exporting Fonts", async h =>
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
                            var baseField = FontHelper.GetByteArrayFont(workspace, cont);
                            if (baseField == null)
                            {
                                errorBuilder.AppendLine($"[{model.Name}]: Failed to read base field");
                                continue;
                            }

                            var fontDataField = baseField["m_FontData"];
                            var arrayField = fontDataField.IsDummy ? null : fontDataField["Array"];
                            byte[]? fontBytes = arrayField?.AsByteArray;

                            if (fontBytes == null || fontBytes.Length == 0)
                            {
                                emptyCount++;
                                continue;
                            }

                            bool isOtf = FontHelper.IsDataOtf(fontBytes);
                            string ext = isOtf ? ".otf" : ".ttf";

                            string fileName = $"{AssetNameUtils.getSaveFileName(model)}{ext}";
                            string fullPath = Path.Combine(targetFolder, fileName);

                            File.WriteAllBytes(fullPath, fontBytes);
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

            string resultMsg = emptyCount > 0
                ? $"Exported {successCount}/{total} Fonts ({emptyCount} skipped: empty/system font)."
                : $"Exported {successCount}/{total} Fonts.";

            NotificationManager.Success(resultMsg, "Batch Export Finished");
        }
        #endregion
    }
}