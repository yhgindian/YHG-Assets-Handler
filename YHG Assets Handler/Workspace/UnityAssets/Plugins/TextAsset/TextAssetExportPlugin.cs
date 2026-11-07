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

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.TextAsset
{
    public sealed class TextAssetExportPlugin : IInternalAssetPlugin
    {
        public string DisplayName => "Export TextAsset";
        public int TargetClassId => (int)AssetClassID.TextAsset; // 49
        public PluginTargetMode TargetMode => PluginTargetMode.Any;

        public async Task ExecuteAsync(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets)
        {
            if (selectedAssets == null || selectedAssets.Count == 0) return;

            var validAssets = new List<UnityAssetObjectModel>();
            for (int i = 0; i < selectedAssets.Count; i++)
            {
                var model = selectedAssets[i];
                if (model.TypeClass == AssetClassID.TextAsset)
                {
                    validAssets.Add(model);
                }
            }

            if (validAssets.Count == 0)
            {
                NotificationManager.Warning("Selected items are not TextAsset.", "Export TextAsset");
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

            var sfd = new SaveFileDialog
            {
                Title = "Export TextAsset",
                FileName = $"{AssetNameUtils.getSaveFileName(assetModel)}.txt",
                Filter = "Text File (*.txt)|*.txt|Bytes File (*.bytes)|*.bytes|JSON File (*.json)|*.json|All Files (*.*)|*.*"
            };

            if (sfd.ShowDialog() != true) return;
            string outputPath = sfd.FileName;

            bool ok = await ProcessManager.RunAsync("Exporting TextAsset", async h =>
            {
                await Task.Run(() =>
                {
                    h.Token.ThrowIfCancellationRequested();
                    h.Report(0, $"Reading {assetModel.Name}...");

                    byte[]? scriptBytes = ExtractScriptBytes(workspace, cont);
                    if (scriptBytes == null)
                    {
                        throw new InvalidOperationException("Failed to read 'm_Script' data from TextAsset.");
                    }

                    h.Report(50, $"Writing file...");
                    File.WriteAllBytes(outputPath, scriptBytes);

                    h.Report(100, "Completed");
                }, h.Token);
            });

            if (ok)
            {
                NotificationManager.Success(assetModel.Name, "TextAsset exported successfully");
            }
        }
        #endregion

        #region Batch Export
        private static async Task BatchExportAsync(UnityAssetWorkspace workspace, List<UnityAssetObjectModel> assets)
        {
            var ofd = new OpenFolderDialog
            {
                Title = "Select Destination Folder for TextAssets"
            };

            if (ofd.ShowDialog() != true) return;
            string targetFolder = ofd.FolderName;

            int total = assets.Count;
            int successCount = 0;
            var errorBuilder = new StringBuilder();

            await ProcessManager.RunAsync("Batch Exporting TextAssets", async h =>
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
                            byte[]? scriptBytes = ExtractScriptBytes(workspace, cont);
                            if (scriptBytes == null)
                            {
                                errorBuilder.AppendLine($"[{model.Name}]: Failed to read m_Script");
                                continue;
                            }

                            string fileName = $"{AssetNameUtils.getSaveFileName(model)}.txt";
                            string fullPath = Path.Combine(targetFolder, fileName);

                            File.WriteAllBytes(fullPath, scriptBytes);
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

            NotificationManager.Success($"Exported {successCount}/{total} TextAssets.", "Batch Export Finished");
        }
        #endregion

        #region Helper
        private static byte[]? ExtractScriptBytes(UnityAssetWorkspace workspace, AssetContainer cont)
        {
            if (!cont.HasValueField)
            {
                var fullCont = workspace.GetAssetContainer(cont.FileInstance, 0, cont.PathId);
                if (fullCont == null) return null;
                cont = fullCont;
            }

            if (cont.BaseValueField is not { } baseField)
                return null;

            var scriptField = baseField["m_Script"];
            if (scriptField.IsDummy)
                return null;

            return scriptField.AsByteArray;
        }
        #endregion
    }
}