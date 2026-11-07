using AssetsHandler.Core;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Microsoft.Win32;
using StbImageWriteSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.Utils.Unity.Texture2D;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Texture2D
{
    public sealed class TextureExportPlugin : IInternalAssetPlugin
    {
        public string DisplayName => "Export Texture2D/Sprite";
        public int TargetClassId => 28;
        public PluginTargetMode TargetMode => PluginTargetMode.Any;

        public async Task ExecuteAsync(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets)
        {
            if (selectedAssets == null || selectedAssets.Count == 0) return;

            var validAssets = new List<UnityAssetObjectModel>();
            for (int i = 0; i < selectedAssets.Count; i++)
            {
                var model = selectedAssets[i];
                if (model.TypeClass is AssetClassID.Texture2D or AssetClassID.Sprite)
                {
                    validAssets.Add(model);
                }
            }

            if (validAssets.Count == 0)
            {
                NotificationManager.Warning("Selected items are not Texture2D or Sprite.", "Export Texture");
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
                Title = "Export Texture",
                FileName = $"{AssetNameUtils.getSaveFileName(assetModel)}.png",
                Filter = "PNG Image (*.png)|*.png|TGA Image (*.tga)|*.tga|JPEG Image (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp"
            };

            if (sfd.ShowDialog() != true) return;
            string outputPath = sfd.FileName;

            bool ok = await ProcessManager.RunAsync("Exporting Texture", async h =>
            {
                await Task.Run(() =>
                {
                    h.Token.ThrowIfCancellationRequested();
                    h.Report(0, $"Decoding {assetModel.Name} via TexToolHelper...");

                    ExportSingleTexture(workspace, cont, outputPath);
                    h.Report(100, "Completed");
                }, h.Token);
            });

            if (ok)
            {
                NotificationManager.Success(assetModel.Name, "Texture exported successfully");
            }
        }
        #endregion

        #region Batch Export
        private static async Task BatchExportAsync(UnityAssetWorkspace workspace, List<UnityAssetObjectModel> assets)
        {
            var ofd = new OpenFolderDialog
            {
                Title = "Select Destination Folder for Textures"
            };

            if (ofd.ShowDialog() != true) return;
            string targetFolder = ofd.FolderName;

            int total = assets.Count;
            int successCount = 0;
            int emptyCount = 0;
            var errorBuilder = new StringBuilder();

            await ProcessManager.RunAsync("Batch Exporting Textures", async h =>
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
                            string fileName = $"{AssetNameUtils.getSaveFileName(model)}.png";
                            string fullPath = Path.Combine(targetFolder, fileName);

                            if (ExportSingleTexture(workspace, cont, fullPath, out bool isEmpty))
                            {
                                successCount++;
                            }
                            else if (isEmpty)
                            {
                                emptyCount++;
                            }
                            else
                            {
                                errorBuilder.AppendLine($"Failed to export {model.Name}");
                            }
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
                ? $"Exported {successCount}/{total} images ({emptyCount} skipped due to 0x0 size)."
                : $"Exported {successCount}/{total} textures.";

            NotificationManager.Success(resultMsg, "Batch Export Finished");
        }
        #endregion

        #region Core Native Extraction (TexToolHelper + STB)
        private static void ExportSingleTexture(UnityAssetWorkspace workspace, AssetContainer cont, string outPath)
        {
            if (!ExportSingleTexture(workspace, cont, outPath, out bool isEmpty))
            {
                if (isEmpty)
                    throw new InvalidOperationException("Texture has 0x0 dimensions (not exportable).");

                throw new InvalidOperationException("Failed to decode texture from binary data using native TexToolHelper.");
            }
        }

        private static bool ExportSingleTexture(UnityAssetWorkspace workspace, AssetContainer cont, string outPath, out bool isEmptyTexture)
        {
            isEmptyTexture = false;

            if (!cont.HasValueField)
            {
                var fullCont = workspace.GetAssetContainer(cont.FileInstance, 0, cont.PathId);
                if (fullCont == null)
                    return false;

                cont = fullCont;
            }

            if (cont?.BaseValueField is not { } baseField)
                return false;

            AssetTypeValueField? textureTargetField = baseField;

            var texFile = TextureFile.ReadTextureFile(textureTargetField);

            if (texFile.m_PlatformBlob.Length != 0)
            {
                TextureHelper.SwizzleOptIn(texFile, cont.FileInstance.file);
            }

            if (texFile.m_Width == 0 && texFile.m_Height == 0)
            {
                isEmptyTexture = true;
                return false;
            }

            byte[] encTextureData = texFile.FillPictureData(cont.FileInstance);
            if (encTextureData == null || encTextureData.Length == 0)
                return false;

            int width = texFile.m_Width;
            int height = texFile.m_Height;

            byte[] rawRgba = new byte[width * height * 4];

            unsafe
            {
                fixed (byte* pInput = encTextureData)
                fixed (byte* pOutput = rawRgba)
                {
                    bool ok = PInvoke.DecodeTextureNative(
                        (IntPtr)pInput,
                        (UIntPtr)encTextureData.Length,
                        width,
                        height,
                        texFile.m_TextureFormat,
                        (IntPtr)pOutput,
                        null
                    );

                    if (!ok)
                        return false;
                }
            }

            FlipVertically(rawRgba, width, height);

            byte[] finalPixels = rawRgba;
            int finalWidth = width;
            int finalHeight = height;

            using var outStream = File.Open(outPath, FileMode.Create, FileAccess.Write, FileShare.None);
            SaveImageViaStb(finalPixels, finalWidth, finalHeight, outStream, outPath);

            return true;
        }

        private static void FlipVertically(byte[] data, int width, int height)
        {
            int rowBytes = width * 4;
            byte[] tempRow = new byte[rowBytes];

            for (int y = 0; y < height / 2; y++)
            {
                int topIdx = y * rowBytes;
                int bottomIdx = (height - 1 - y) * rowBytes;

                Buffer.BlockCopy(data, topIdx, tempRow, 0, rowBytes);
                Buffer.BlockCopy(data, bottomIdx, data, topIdx, rowBytes);
                Buffer.BlockCopy(tempRow, 0, data, bottomIdx, rowBytes);
            }
        }

        private static byte[] CropRgba(byte[] src, int srcWidth, int x, int y, int w, int h)
        {
            byte[] dst = new byte[w * h * 4];
            int srcRowBytes = srcWidth * 4;
            int dstRowBytes = w * 4;

            for (int row = 0; row < h; row++)
            {
                int srcOffset = (y + row) * srcRowBytes + (x * 4);
                int dstOffset = row * dstRowBytes;
                Buffer.BlockCopy(src, srcOffset, dst, dstOffset, dstRowBytes);
            }

            return dst;
        }

        private static void SaveImageViaStb(byte[] rgba, int width, int height, Stream stream, string path)
        {
            var writer = new ImageWriter();
            string ext = Path.GetExtension(path).ToLowerInvariant();

            switch (ext)
            {
                case ".bmp":
                    writer.WriteBmp(rgba, width, height, ColorComponents.RedGreenBlueAlpha, stream);
                    break;
                case ".jpg":
                case ".jpeg":
                    byte[] rgb = StripAlpha(rgba, width * height);
                    writer.WriteJpg(rgb, width, height, ColorComponents.RedGreenBlue, stream, 95);
                    break;
                case ".tga":
                    writer.WriteTga(rgba, width, height, ColorComponents.RedGreenBlueAlpha, stream);
                    break;
                case ".png":
                default:
                    writer.WritePng(rgba, width, height, ColorComponents.RedGreenBlueAlpha, stream);
                    break;
            }
        }

        private static byte[] StripAlpha(byte[] rgba, int pixelCount)
        {
            byte[] rgb = new byte[pixelCount * 3];
            for (int i = 0, j = 0; i < rgba.Length; i += 4, j += 3)
            {
                rgb[j] = rgba[i];
                rgb[j + 1] = rgba[i + 1];
                rgb[j + 2] = rgba[i + 2];
            }
            return rgb;
        }
        #endregion
    }
}