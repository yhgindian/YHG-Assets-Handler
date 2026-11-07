using AssetsHandler.Core;
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

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Font
{
    public sealed class FontImportPlugin : IInternalAssetPlugin
    {
        public string DisplayName => "Import Font";
        public int TargetClassId => (int)AssetClassID.Font; // 128
        public PluginTargetMode TargetMode => PluginTargetMode.Any;

        public async Task ExecuteAsync(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets)
        {
            if (selectedAssets == null || selectedAssets.Count == 0) return;

            var validAssets = selectedAssets
                .Where(m => m.TypeClass == AssetClassID.Font && m.assetContainer?.FileInstance != null)
                .ToList();

            if (validAssets.Count == 0)
            {
                NotificationManager.Warning("Selected items are not Font assets.", "Import Font");
                return;
            }

            if (validAssets.Count == 1)
            {
                await SingleImportAsync(workspace, validAssets[0]);
            }
            else
            {
                await BatchImportAsync(workspace, validAssets);
            }
        }

        #region Single Import
        private static async Task SingleImportAsync(UnityAssetWorkspace workspace, UnityAssetObjectModel assetModel)
        {
            var cont = assetModel.assetContainer;
            if (cont?.FileInstance == null) return;

            var ofd = new OpenFileDialog
            {
                Title = "Select TrueType/OpenType Font file to import",
                Filter = "Font Files (*.ttf;*.otf)|*.ttf;*.otf|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() != true || !File.Exists(ofd.FileName)) return;
            string importPath = ofd.FileName;

            bool ok = await ProcessManager.RunAsync("Importing Font", async h =>
            {
                await Task.Run(() =>
                {
                    h.Token.ThrowIfCancellationRequested();
                    h.Report(0, $"Reading {Path.GetFileName(importPath)}...");

                    byte[] newFontBytes = File.ReadAllBytes(importPath);

                    h.Report(50, $"Injecting font data into {assetModel.Name}...");
                    if (!UpdateFontAssetData(workspace, cont, newFontBytes))
                    {
                        throw new InvalidOperationException("Failed to update Font m_FontData array.");
                    }

                    h.Report(100, "Completed");
                }, h.Token);
            });

            if (ok)
            {
                NotificationManager.Success(assetModel.Name, "Font imported successfully");
            }
        }
        #endregion

        #region Batch Import (Using BatchImportView)
        private static async Task BatchImportAsync(UnityAssetWorkspace workspace, List<UnityAssetObjectModel> assets)
        {
            var dirDlg = new OpenFolderDialog
            {
                Title = "Select Folder Containing .ttf or .otf Files"
            };

            if (dirDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dirDlg.FolderName))
                return;

            string directory = dirDlg.FolderName;

            var validContainers = assets
                .Select(x => x.assetContainer)
                .OfType<AssetContainer>()
                .ToList();

            var extensions = new List<string> { "ttf", "otf" };

            var vm = new BatchImportViewModel(workspace, validContainers, directory, extensions);
            var view = new BatchImportView(vm)
            {
                Owner = Application.Current.MainWindow
            };

            if (view.ShowDialog() != true)
                return;

            if (vm.ConfirmedImports.Count == 0)
            {
                NotificationManager.Info("No matching font files found for the selected assets.", "Import Font");
                return;
            }

            var targets = vm.ConfirmedImports.ToList();
            int total = targets.Count;
            int imported = 0, failed = 0;

            bool completed = await ProcessManager.RunAsync("Batch Importing Fonts", async h =>
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        h.Token.ThrowIfCancellationRequested();

                        var batchInfo = targets[i];
                        h.Report((i + 1) * 100.0 / total, $"Importing ({i + 1}/{total}): {batchInfo.AssetName}");

                        try
                        {
                            if (batchInfo.Container != null && File.Exists(batchInfo.ImportFilePath))
                            {
                                byte[] newFontBytes = File.ReadAllBytes(batchInfo.ImportFilePath);
                                if (UpdateFontAssetData(workspace, batchInfo.Container, newFontBytes))
                                {
                                    imported++;
                                }
                                else
                                {
                                    failed++;
                                    Status.Write($"Failed to update m_FontData for {batchInfo.AssetName}", StatusType.Warning);
                                }
                            }
                            else
                            {
                                failed++;
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            Status.Write($"Font import exception for {batchInfo.AssetName}: {ex.Message}", StatusType.Error);
                        }
                    }

                    h.Report(100, $"Finished: {imported}/{total} fonts imported.");
                }, h.Token);
            });

            if (!completed)
            {
                NotificationManager.Warning($"Stopped after {imported}/{total} fonts.", "Batch Font Import");
            }
            else if (failed == 0)
            {
                NotificationManager.Success($"Imported {imported}/{total} fonts successfully.", "Batch Font Import");
            }
            else
            {
                NotificationManager.Warning($"{imported} imported, {failed} failed. Check the console.", "Batch Font Import");
            }
        }
        #endregion

        #region Helpers
        private static bool UpdateFontAssetData(UnityAssetWorkspace workspace, AssetContainer cont, byte[] fontBytes)
        {
            var baseField = FontHelper.GetByteArrayFont(workspace, cont);
            if (baseField == null) return false;

            var fontDataField = baseField["m_FontData"];
            if (fontDataField.IsDummy) return false;

            var arrayField = fontDataField["Array"];
            if (arrayField.IsDummy) return false;

            arrayField.AsByteArray = fontBytes;

            byte[] updatedRaw = baseField.WriteToByteArray();
            workspace.AddOrUpdateAsset(cont, updatedRaw);
            return true;
        }
        #endregion
    }
}