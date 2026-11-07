using AssetsHandler.Core;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.Workspace.UnityAssets;
using YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Mesh
{
    [Flags]
    public enum MeshExportFormat
    {
        Obj = 1 << 0,
        Glb = 1 << 1,
        Dae = 1 << 2,
        Gltf = 1 << 3,
        Fbx = 1 << 4
    }

    public class MeshExportPlugin : IInternalAssetPlugin
    {
        public string DisplayName => "Export Mesh";
        public int TargetClassId => (int)AssetClassID.Mesh; // 43
        public PluginTargetMode TargetMode => PluginTargetMode.Any;

        public MeshExportFormat ExportFormat { get; set; } = MeshExportFormat.Obj;

        public async Task ExecuteAsync(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets)
        {
            if (selectedAssets == null || selectedAssets.Count == 0) return;

            var validAssets = selectedAssets
                .Where(m => m.TypeClass == AssetClassID.Mesh && m.assetContainer?.FileInstance != null)
                .ToList();

            if (validAssets.Count == 0)
            {
                NotificationManager.Warning("Selected items are not Mesh assets.", "Export Mesh");
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
        private async Task SingleExportAsync(UnityAssetWorkspace workspace, UnityAssetObjectModel assetModel)
        {
            var cont = assetModel.assetContainer;
            if (cont?.FileInstance == null) return;

            string safeName = AssetNameUtils.getSaveFileName(cont, assetModel.Name);
            if (string.IsNullOrWhiteSpace(safeName)) safeName = $"Mesh_{cont.PathId}";

            string extension = GetExtensionForFormat(ExportFormat);
            var sfd = new SaveFileDialog
            {
                Title = "Export Mesh File",
                FileName = $"{safeName}.{extension}",
                Filter = GetFilterForFormat(ExportFormat)
            };

            if (sfd.ShowDialog() != true) return;
            string exportPath = sfd.FileName;
            string detectedExt = Path.GetExtension(exportPath).TrimStart('.').ToLowerInvariant();

            bool ok = await ProcessManager.RunAsync("Exporting Mesh", async h =>
            {
                await Task.Run(() =>
                {
                    h.Token.ThrowIfCancellationRequested();
                    h.Report(15, $"Reading TypeTree {assetModel.Name}...");

                    var meshField = GetMeshBaseField(workspace, cont);
                    if (meshField == null || meshField.IsDummy)
                        throw new InvalidOperationException("Failed to read Mesh base field from workspace.");

                    var unityVersion = cont.FileInstance.file.Metadata.UnityVersion;
                    var meshFile = new MeshFile(new UnityVersion(unityVersion), meshField);

                    h.Report(50, $"Exporting via Assimp ({detectedExt.ToUpper()})...");
                    ExportMeshDispatch(meshFile, detectedExt, exportPath);

                    h.Report(100, "Export completed.");
                }, h.Token);
            });

            if (ok)
            {
                NotificationManager.Success(assetModel.Name, $"Exported successfully as {detectedExt.ToUpper()}");
            }
        }
        #endregion

        #region Batch Export
        private async Task BatchExportAsync(UnityAssetWorkspace workspace, List<UnityAssetObjectModel> assets)
        {
            var dirDlg = new OpenFolderDialog
            {
                Title = "Select Destination Folder for Exported Meshes"
            };

            if (dirDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dirDlg.FolderName))
                return;

            string targetFolder = dirDlg.FolderName;
            string extension = GetExtensionForFormat(ExportFormat);

            int total = assets.Count;
            int exported = 0;
            int failed = 0;
            var errors = new StringBuilder();

            bool ok = await ProcessManager.RunAsync("Batch Exporting Meshes", async h =>
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        if (h.Token.IsCancellationRequested) break;

                        var model = assets[i];
                        var cont = model.assetContainer;
                        h.Report((i + 1) * 100.0 / total, $"Exporting ({i + 1}/{total}): {model.Name}");

                        if (cont?.FileInstance == null)
                        {
                            failed++;
                            continue;
                        }

                        try
                        {
                            var meshField = GetMeshBaseField(workspace, cont);
                            if (meshField == null || meshField.IsDummy)
                            {
                                errors.AppendLine($"[{model.Name}]: Base field missing or dummy.");
                                failed++;
                                continue;
                            }

                            var unityVersion = cont.FileInstance.file.Metadata.UnityVersion;
                            var meshFile = new MeshFile(new UnityVersion(unityVersion), meshField);

                            string baseSaveName = AssetNameUtils.getSaveFileName(model);
                            string cleanName = PathUtils.ReplaceInvalidPathChars(baseSaveName);
                            if (string.IsNullOrWhiteSpace(cleanName)) cleanName = $"Mesh_{cont.PathId}";

                            string filePath = Path.Combine(targetFolder, $"{cleanName}.{extension}");
                            ExportMeshDispatch(meshFile, extension, filePath);
                            exported++;
                        }
                        catch (Exception ex)
                        {
                            errors.AppendLine($"[{model.Name}]: {ex.Message}");
                            failed++;
                        }
                    }

                    h.Report(100, $"Finished: {exported}/{total} meshes exported.");
                }, h.Token);
            });

            if (errors.Length > 0)
            {
                Status.Write(errors.ToString(), StatusType.Warning);
            }

            if (!ok)
            {
                NotificationManager.Warning($"Export cancelled after {exported}/{total} meshes.", "Mesh Export");
            }
            else if (failed == 0)
            {
                NotificationManager.Success($"Exported {exported}/{total} meshes successfully.", "Batch Mesh Export");
            }
            else
            {
                NotificationManager.Warning($"{exported} exported, {failed} failed. See status log.", "Batch Mesh Export");
            }
        }
        #endregion

        #region Helpers
        private static AssetTypeValueField? GetMeshBaseField(UnityAssetWorkspace workspace, AssetContainer cont)
        {
            if (!cont.HasValueField)
            {
                var fullCont = workspace.GetAssetContainer(cont.FileInstance, 0, cont.PathId);
                if (fullCont == null) return null;
                cont = fullCont;
            }

            return cont.BaseValueField;
        }

        private static void ExportMeshDispatch(MeshFile meshFile, string formatExtension, string targetPath)
        {
            if (formatExtension == "fbx")
            {
                throw new NotImplementedException("Autodesk FBX export will run through the native pipeline.");
            }

            meshFile.ExportWithAssimp(targetPath, formatExtension);
        }

        private static string GetExtensionForFormat(MeshExportFormat format)
        {
            return format switch
            {
                MeshExportFormat.Glb => "glb",
                MeshExportFormat.Obj => "obj",
                MeshExportFormat.Dae => "dae",
                MeshExportFormat.Gltf => "gltf",
                MeshExportFormat.Fbx => "fbx",
                _ => "glb"
            };
        }

        private static string GetFilterForFormat(MeshExportFormat format)
        {
            return format switch
            {
                MeshExportFormat.Glb => "glTF 2.0 Binary (*.glb)|*.glb|Wavefront OBJ (*.obj)|*.obj|Collada DAE (*.dae)|*.dae|glTF 2.0 JSON (*.gltf)|*.gltf|All Files (*.*)|*.*",
                MeshExportFormat.Obj => "Wavefront OBJ (*.obj)|*.obj|glTF 2.0 Binary (*.glb)|*.glb|Collada DAE (*.dae)|*.dae|All Files (*.*)|*.*",
                MeshExportFormat.Dae => "Collada DAE (*.dae)|*.dae|glTF 2.0 Binary (*.glb)|*.glb|Wavefront OBJ (*.obj)|*.obj|All Files (*.*)|*.*",
                MeshExportFormat.Fbx => "Autodesk FBX (*.fbx)|*.fbx|All Files (*.*)|*.*",
                _ => "glTF 2.0 Binary (*.glb)|*.glb|Wavefront OBJ (*.obj)|*.obj|Collada DAE (*.dae)|*.dae|All Files (*.*)|*.*"
            };
        }
        #endregion
    }
}