using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using StbImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Workspace.UnityAssets;
using ColorSpaceEnm = YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Texture2D.ColorSpace;
using FilterModeEnm = YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Texture2D.FilterMode;
using TextureFormatEnm = AssetsTools.NET.Texture.TextureFormat;
using WrapModeEnm = YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Texture2D.WrapMode;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Texture2D
{
    public partial class EditTextureViewModel : ObservableObject
    {
        private readonly UnityAssetWorkspace _workspace;
        private readonly IReadOnlyList<UnityAssetObjectModel> _selectedAssets;

        [ObservableProperty]
        private bool _isSingleTexture;

        // 🔹 Form Field Properties
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(NameWatermark))]
        private string? _name = string.Empty;

        [ObservableProperty]
        private TextureFormatEnm? _textureFormat = TextureFormatEnm.RGBA32;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UsingMipsThreeOn))]
        private bool? _usingMips = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsReadableThreeOn))]
        private bool? _isReadable = false;

        [ObservableProperty]
        private FilterModeEnm? _filterMode = FilterModeEnm.Point;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FilteringWatermark))]
        private string? _filteringString = "0";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MipBiasWatermark))]
        private string? _mipBiasString = "0";

        [ObservableProperty]
        private WrapModeEnm? _wrapModeU = WrapModeEnm.Repeat;

        [ObservableProperty]
        private WrapModeEnm? _wrapModeV = WrapModeEnm.Repeat;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LightMapFormatWatermark))]
        private string? _lightMapFormatString = "0";

        [ObservableProperty]
        private ColorSpaceEnm? _colorSpace = ColorSpaceEnm.Gamma;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LoadTextureCaption))]
        private string? _loadTexturePath;

        // 🔹 Default snapshots for Reset Buttons (0 - 11)
        private string? _defaultName;
        private TextureFormatEnm? _defaultTextureFormat;
        private bool? _defaultUsingMips;
        private bool? _defaultIsReadable;
        private FilterModeEnm? _defaultFilterMode;
        private string? _defaultFilteringString;
        private string? _defaultMipBiasString;
        private WrapModeEnm? _defaultWrapModeU;
        private WrapModeEnm? _defaultWrapModeV;
        private string? _defaultLightMapFormatString;
        private ColorSpaceEnm? _defaultColorSpace;

        public string NameWatermark => Name is null ? "(Multiple values)" : string.Empty;
        public string FilteringWatermark => FilteringString is null ? "(Multiple values)" : string.Empty;
        public string MipBiasWatermark => MipBiasString is null ? "(Multiple values)" : string.Empty;
        public string LightMapFormatWatermark => LightMapFormatString is null ? "(Multiple values)" : string.Empty;

        public bool UsingMipsThreeOn => UsingMips is null;
        public bool IsReadableThreeOn => IsReadable is null;

        public static TextureFormatEnm[] TextureFormats => Enum.GetValues<TextureFormatEnm>();
        public static FilterModeEnm[] FilterModes => Enum.GetValues<FilterModeEnm>();
        public static WrapModeEnm[] WrapModes => Enum.GetValues<WrapModeEnm>();
        public static ColorSpaceEnm[] ColorSpaces => Enum.GetValues<ColorSpaceEnm>();

        public string LoadTextureCaption => LoadTexturePath is null ? string.Empty : $" ({Path.GetFileName(LoadTexturePath)})";

        public event Action? RequestClose;

        private readonly List<(AssetContainer Container, AssetTypeValueField BaseField, TextureFile File)> _textures = new();

        public EditTextureViewModel(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets)
        {
            _workspace = workspace;
            _selectedAssets = selectedAssets;
            IsSingleTexture = selectedAssets.Count == 1;

            foreach (var assetModel in selectedAssets)
            {
                var cont = assetModel.assetContainer;
                if (cont?.FileInstance == null) continue;

                if (!cont.HasValueField)
                {
                    cont = workspace.GetAssetContainer(cont.FileInstance, 0, cont.PathId);
                }

                if (cont?.BaseValueField is not { } baseField)
                    continue;

                try
                {
                    var texFile = TextureFile.ReadTextureFile(baseField);
                    _textures.Add((cont, baseField, texFile));
                }
                catch
                {
                    // Ignore unsupported or malformed headers
                }
            }

            SetDefaultValues();
        }

        public void SetDefaultValues()
        {
            if (_textures.Count == 0) return;

            var first = _textures[0].File;
            var settings = first.m_TextureSettings;

            string? name = first.m_Name;
            TextureFormatEnm? textureFormat = (TextureFormatEnm)first.m_TextureFormat;
            bool? usingMips = first.m_MipMap;
            bool? isReadable = first.m_IsReadable;
            FilterModeEnm? filterMode = (FilterModeEnm)settings.m_FilterMode;
            int? filtering = settings.m_Aniso;
            float? mipBias = settings.m_MipBias;
            WrapModeEnm? wrapModeU = (WrapModeEnm)settings.m_WrapU;
            WrapModeEnm? wrapModeV = (WrapModeEnm)settings.m_WrapV;
            int? lightMapFormat = first.m_LightmapFormat;
            ColorSpaceEnm? colorSpace = (ColorSpaceEnm)first.m_ColorSpace;

            if (_textures.Count > 1)
            {
                for (int i = 1; i < _textures.Count; i++)
                {
                    var cur = _textures[i].File;
                    var curSet = cur.m_TextureSettings;

                    if (name != cur.m_Name) name = null;
                    if (textureFormat != (TextureFormatEnm)cur.m_TextureFormat) textureFormat = null;
                    if (usingMips != cur.m_MipMap) usingMips = null;
                    if (isReadable != cur.m_IsReadable) isReadable = null;
                    if (filterMode != (FilterModeEnm)curSet.m_FilterMode) filterMode = null;
                    if (filtering != curSet.m_Aniso) filtering = null;
                    if (mipBias != curSet.m_MipBias) mipBias = null;
                    if (wrapModeU != (WrapModeEnm)curSet.m_WrapU) wrapModeU = null;
                    if (wrapModeV != (WrapModeEnm)curSet.m_WrapV) wrapModeV = null;
                    if (lightMapFormat != cur.m_LightmapFormat) lightMapFormat = null;
                    if (colorSpace != (ColorSpaceEnm)cur.m_ColorSpace) colorSpace = null;
                }
            }

            _defaultName = Name = name;
            _defaultTextureFormat = TextureFormat = textureFormat;
            _defaultUsingMips = UsingMips = usingMips;
            _defaultIsReadable = IsReadable = isReadable;
            _defaultFilterMode = FilterMode = filterMode;
            _defaultFilteringString = FilteringString = filtering?.ToString();
            _defaultMipBiasString = MipBiasString = mipBias?.ToString();
            _defaultWrapModeU = WrapModeU = wrapModeU;
            _defaultWrapModeV = WrapModeV = wrapModeV;
            _defaultLightMapFormatString = LightMapFormatString = lightMapFormat?.ToString();
            _defaultColorSpace = ColorSpace = colorSpace;
            LoadTexturePath = null;
        }

        [RelayCommand]
        private void ResetToDefault(object? param)
        {
            if (param is not string paramStr || !int.TryParse(paramStr, out int index))
                return;

            switch (index)
            {
                case 0: Name = _defaultName; break;
                case 1: TextureFormat = _defaultTextureFormat; break;
                case 2: UsingMips = _defaultUsingMips; break;
                case 3: IsReadable = _defaultIsReadable; break;
                case 4: FilterMode = _defaultFilterMode; break;
                case 5: FilteringString = _defaultFilteringString; break;
                case 6: MipBiasString = _defaultMipBiasString; break;
                case 7: WrapModeU = _defaultWrapModeU; break;
                case 8: WrapModeV = _defaultWrapModeV; break;
                case 9: LightMapFormatString = _defaultLightMapFormatString; break;
                case 10: ColorSpace = _defaultColorSpace; break;
                case 11: LoadTexturePath = null; break;
            }
        }

        [RelayCommand]
        private void LoadSingleTexture()
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select Image File",
                Filter = "Common Images (*.png;*.jpg;*.jpeg;*.tga;*.bmp)|*.png;*.jpg;*.jpeg;*.tga;*.bmp|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() == true && File.Exists(ofd.FileName))
            {
                LoadTexturePath = ofd.FileName;
            }
        }

        [RelayCommand]
        private async Task SaveChanges()
        {
            // 🔹 1. Validations
            int? filtering = null;
            if (FilteringString is not null)
            {
                if (!int.TryParse(FilteringString, out int fVal))
                {
                    NotificationManager.Warning("Anisotropic Filtering must be an integer.", "Validation Error");
                    return;
                }
                filtering = fVal;
            }

            float? mipBias = null;
            if (MipBiasString is not null)
            {
                if (!float.TryParse(MipBiasString, out float mbVal))
                {
                    NotificationManager.Warning("Mipmap Bias must be a valid number.", "Validation Error");
                    return;
                }
                mipBias = mbVal;
            }

            int? lightMapFormat = null;
            if (LightMapFormatString is not null)
            {
                if (!int.TryParse(LightMapFormatString, out int lVal))
                {
                    NotificationManager.Warning("Lightmap Format must be an integer.", "Validation Error");
                    return;
                }
                lightMapFormat = lVal;
            }

            RequestClose?.Invoke();

            // 🔹 2. Background encoding and workspace update
            bool singleTextureEdit = _textures.Count == 1 && LoadTexturePath is not null;
            string loadPath = LoadTexturePath ?? string.Empty;

            var snapshot = _textures.ToList();
            int total = snapshot.Count;

            await ProcessManager.RunAsync("Updating Textures", async h =>
            {
                await Task.Run(() =>
                {
                    int successCount = 0;
                    var errorBuilder = new StringBuilder();

                    for (int i = 0; i < total; i++)
                    {
                        h.Token.ThrowIfCancellationRequested();

                        var (container, baseField, tex) = snapshot[i];
                        string assetName = $"{container.FileInstance.name}/{container.PathId}";

                        double baseProgress = (double)i / total * 100.0;
                        double stepWeight = 100.0 / total;

                        h.Report(baseProgress, $"[{i + 1}/{total}] Preparing: {tex.m_Name}");

                        // 🔥 Native callback linked directly to progress reporting
                        PInvoke.ProgressCallback nativeCb = (float progress, string status) =>
                        {
                            double currentProgress = baseProgress + (Math.Clamp((double)progress, 0.0, 1.0) * stepWeight);
                            h.Report(currentProgress, $"[{i + 1}/{total}] {tex.m_Name}: {status}");
                        };

                        try
                        {
                            byte[]? rawRgbaPixels = null;
                            int encodeWidth = tex.m_Width;
                            int encodeHeight = tex.m_Height;

                            if (singleTextureEdit)
                            {
                                h.Report(baseProgress + (stepWeight * 0.1), $"[{i + 1}/{total}] Loading: {Path.GetFileName(loadPath)}");

                                using var fs = File.OpenRead(loadPath);
                                var img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha);
                                rawRgbaPixels = img.Data;
                                encodeWidth = img.Width;
                                encodeHeight = img.Height;

                                FlipVertically(rawRgbaPixels, encodeWidth, encodeHeight);
                            }
                            else
                            {
                                bool needToReencode = false;
                                if (TextureFormat is not null)
                                    needToReencode |= tex.m_TextureFormat != (int)TextureFormat.Value;

                                if (UsingMips is not null)
                                {
                                    bool usingMipsVal = UsingMips.Value;
                                    needToReencode |= usingMipsVal ? (tex.m_MipCount <= 1) : (tex.m_MipCount > 1);
                                }

                                if (needToReencode)
                                {
                                    h.Report(baseProgress + (stepWeight * 0.05), $"[{i + 1}/{total}] Reading picture data: {tex.m_Name}");

                                    var texOrigEncBytes = tex.FillPictureData(container.FileInstance);
                                    if (texOrigEncBytes is null || texOrigEncBytes.Length == 0)
                                    {
                                        errorBuilder.AppendLine($"[{assetName}]: Failed to fetch picture data.");
                                        continue;
                                    }

                                    rawRgbaPixels = new byte[tex.m_Width * tex.m_Height * 4];
                                    unsafe
                                    {
                                        fixed (byte* pIn = texOrigEncBytes)
                                        fixed (byte* pOut = rawRgbaPixels)
                                        {
                                            bool decOk = PInvoke.DecodeTextureNative(
                                                (IntPtr)pIn,
                                                (UIntPtr)texOrigEncBytes.Length,
                                                tex.m_Width,
                                                tex.m_Height,
                                                tex.m_TextureFormat,
                                                (IntPtr)pOut,
                                                nativeCb
                                            );

                                            if (!decOk)
                                            {
                                                errorBuilder.AppendLine($"[{assetName}]: Native decoder failed to decode raw blocks.");
                                                continue;
                                            }
                                        }
                                    }
                                }
                            }

                            // Field updates
                            if (Name is not null) tex.m_Name = Name;
                            if (TextureFormat is not null) tex.m_TextureFormat = (int)TextureFormat.Value;
                            if (UsingMips is not null) tex.m_MipMap = UsingMips.Value;
                            if (IsReadable is not null) tex.m_IsReadable = IsReadable.Value;
                            if (FilterMode is not null) tex.m_TextureSettings.m_FilterMode = (int)FilterMode.Value;
                            if (filtering is not null) tex.m_TextureSettings.m_Aniso = filtering.Value;
                            if (mipBias is not null) tex.m_TextureSettings.m_MipBias = mipBias.Value;
                            if (WrapModeU is not null) tex.m_TextureSettings.m_WrapU = (int)WrapModeU.Value;
                            if (WrapModeV is not null) tex.m_TextureSettings.m_WrapV = (int)WrapModeV.Value;
                            if (lightMapFormat is not null) tex.m_LightmapFormat = lightMapFormat.Value;
                            if (ColorSpace is not null) tex.m_ColorSpace = (int)ColorSpace.Value;

                            // Pixel encoding path
                            if (rawRgbaPixels is not null)
                            {
                                int targetFormat = tex.m_TextureFormat;
                                int quality = 3;
                                int mipCount = tex.m_MipMap ? 0 : 1;
                                int flags = (int)PInvoke.EncodeFlags.SrgbMips;
                                int outMipCount = 0;
                                int outHandle = 0;
                                uint totalBytes = 0;

                                unsafe
                                {
                                    fixed (byte* pPixels = rawRgbaPixels)
                                    {
                                        totalBytes = PInvoke.EncodeTextureNative(
                                            (IntPtr)pPixels,
                                            encodeWidth,
                                            encodeHeight,
                                            targetFormat,
                                            quality,
                                            mipCount,
                                            flags,
                                            out outMipCount,
                                            out outHandle,
                                            nativeCb
                                        );
                                    }
                                }

                                if (totalBytes == 0 || outHandle == 0)
                                {
                                    errorBuilder.AppendLine($"[{assetName}]: Native encoder failed for format {(TextureFormatEnm)targetFormat}.");
                                    continue;
                                }

                                byte[] finalEncoded = new byte[totalBytes];
                                unsafe
                                {
                                    fixed (byte* pDst = finalEncoded)
                                    {
                                        bool copyOk = PInvoke.PickUpAndFree((IntPtr)pDst, totalBytes, outHandle);
                                        if (!copyOk)
                                        {
                                            errorBuilder.AppendLine($"[{assetName}]: Failed to pick up native encoded buffer.");
                                            continue;
                                        }
                                    }
                                }

                                // Sync TextureFile metadata
                                tex.m_MipCount = outMipCount > 0 ? outMipCount : (tex.m_MipMap ? 1 : 1);
                                tex.SetPictureData(finalEncoded, encodeWidth, encodeHeight);
                            }

                            // ⚡ Push back into AssetContainer / AssetsTools
                            tex.WriteTo(baseField);
                            byte[] updatedRaw = baseField.WriteToByteArray();
                            _workspace.AddOrUpdateAsset(container, updatedRaw);

                            GC.KeepAlive(nativeCb);

                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            errorBuilder.AppendLine($"[{assetName}]: {ex.Message}");
                        }
                    }

                    if (errorBuilder.Length > 0)
                    {
                        Status.Write(errorBuilder.ToString(), StatusType.Error);
                    }

                    h.Report(100, $"Finished: {successCount}/{total} textures modified.");
                }, h.Token);
            });

            NotificationManager.Success($"Saved changes for {_textures.Count} texture(s).", "Texture Updated");
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

        [RelayCommand]
        private void Cancel()
        {
            RequestClose?.Invoke();
        }
    }
}