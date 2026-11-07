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

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.TextAsset
{
    public sealed class TextAssetImportPlugin : IInternalAssetPlugin
    {
        public string DisplayName => "Import TextAsset";
        public int TargetClassId => (int)AssetClassID.TextAsset; // 49
        public PluginTargetMode TargetMode => PluginTargetMode.Any;

        public async Task ExecuteAsync(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets)
        {
            if (selectedAssets == null || selectedAssets.Count == 0) return;

            var validAssets = selectedAssets
                .Where(m => m.TypeClass == AssetClassID.TextAsset && m.assetContainer?.FileInstance != null)
                .ToList();

            if (validAssets.Count == 0)
            {
                NotificationManager.Warning("Selected items are not TextAsset.", "Import TextAsset");
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
                Title = "Select TextAsset file to import",
                Filter = "Text & Bytes (*.txt;*.bytes;*.json;*.xml;*.csv)|*.txt;*.bytes;*.json;*.xml;*.csv|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() != true || !File.Exists(ofd.FileName)) return;
            string importPath = ofd.FileName;

            bool ok = await ProcessManager.RunAsync("Importing TextAsset", async h =>
            {
                await Task.Run(() =>
                {
                    h.Token.ThrowIfCancellationRequested();
                    h.Report(0, $"Reading {Path.GetFileName(importPath)}...");

                    byte[] newBytes = File.ReadAllBytes(importPath);

                    h.Report(50, $"Updating asset {assetModel.Name}...");
                    if (!UpdateTextAssetData(workspace, cont, newBytes))
                    {
                        throw new InvalidOperationException("Failed to update TextAsset m_Script field.");
                    }

                    h.Report(100, "Completed");
                }, h.Token);
            });

            if (ok)
            {
                NotificationManager.Success(assetModel.Name, "TextAsset imported successfully");
            }
        }
        #endregion

        #region Batch Import (Using BatchImportView)
        private static async Task BatchImportAsync(UnityAssetWorkspace workspace, List<UnityAssetObjectModel> assets)
        {
            var dirDlg = new OpenFolderDialog
            {
                Title = "Select Folder Containing Imported TextAsset Files"
            };

            if (dirDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dirDlg.FolderName))
                return;

            string directory = dirDlg.FolderName;

            var validContainers = assets
                .Select(x => x.assetContainer)
                .OfType<AssetContainer>()
                .ToList();

            var extensions = new List<string> { "txt", "bytes", "json", "xml", "csv" };

            var vm = new BatchImportViewModel(workspace, validContainers, directory, extensions);
            var view = new BatchImportView(vm)
            {
                Owner = Application.Current.MainWindow
            };

            if (view.ShowDialog() != true)
                return;

            if (vm.ConfirmedImports.Count == 0)
            {
                NotificationManager.Info("No matching text files found for the selected assets.", "Import TextAsset");
                return;
            }

            var targets = vm.ConfirmedImports.ToList();
            int total = targets.Count;
            int imported = 0, failed = 0;

            bool completed = await ProcessManager.RunAsync("Batch Importing TextAssets", async h =>
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
                                byte[] newBytes = File.ReadAllBytes(batchInfo.ImportFilePath);
                                if (UpdateTextAssetData(workspace, batchInfo.Container, newBytes))
                                {
                                    imported++;
                                }
                                else
                                {
                                    failed++;
                                    Status.Write($"Failed to update m_Script for {batchInfo.AssetName}", StatusType.Warning);
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
                            Status.Write($"TextAsset import exception for {batchInfo.AssetName}: {ex.Message}", StatusType.Error);
                        }
                    }

                    h.Report(100, $"Finished: {imported}/{total} text assets imported.");
                }, h.Token);
            });

            if (!completed)
            {
                NotificationManager.Warning($"Stopped after {imported}/{total} text assets.", "Batch TextAsset Import");
            }
            else if (failed == 0)
            {
                NotificationManager.Success($"Imported {imported}/{total} text assets successfully.", "Batch TextAsset Import");
            }
            else
            {
                NotificationManager.Warning($"{imported} imported, {failed} failed. Check the console.", "Batch TextAsset Import");
            }
        }
        #endregion

        #region Helpers
        private static bool UpdateTextAssetData(UnityAssetWorkspace workspace, AssetContainer cont, byte[] newBytes)
        {
            if (!cont.HasValueField)
            {
                var fullCont = workspace.GetAssetContainer(cont.FileInstance, 0, cont.PathId);
                if (fullCont == null) return false;
                cont = fullCont;
            }

            if (cont.BaseValueField is not { } baseField)
                return false;

            var scriptField = baseField["m_Script"];
            if (scriptField.IsDummy)
                return false;

            scriptField.AsByteArray = newBytes;

            byte[] updatedRaw = baseField.WriteToByteArray();
            workspace.AddOrUpdateAsset(cont, updatedRaw);
            return true;
        }
        #endregion
    }
}