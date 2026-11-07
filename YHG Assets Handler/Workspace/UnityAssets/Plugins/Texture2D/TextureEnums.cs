namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Texture2D
{
    public enum FilterMode
    {
        Point = 0,
        Bilinear = 1,
        Trilinear = 2
    }

    public enum WrapMode
    {
        Repeat = 0,
        Clamp = 1,
        Mirror = 2,
        MirrorOnce = 3
    }

    public enum ColorSpace
    {
        Gamma = 0,
        Linear = 1
    }

    public readonly struct EditTextureResult
    {
        public readonly string? Name;
        public readonly AssetsTools.NET.Texture.TextureFormat? TextureFormat;
        public readonly bool? UsingMips;
        public readonly bool? IsReadable;
        public readonly FilterMode? FilterMode;
        public readonly int? Filtering;
        public readonly float? MipBias;
        public readonly WrapMode? WrapModeU;
        public readonly WrapMode? WrapModeV;
        public readonly int? LightMapFormat;
        public readonly ColorSpace? ColorSpace;
        public readonly string? LoadTexturePath;

        public EditTextureResult(
            string? name,
            AssetsTools.NET.Texture.TextureFormat? textureFormat,
            bool? usingMips,
            bool? isReadable,
            FilterMode? filterMode,
            int? filtering,
            float? mipBias,
            WrapMode? wrapModeU,
            WrapMode? wrapModeV,
            int? lightMapFormat,
            ColorSpace? colorSpace,
            string? loadTexturePath)
        {
            Name = name;
            TextureFormat = textureFormat;
            UsingMips = usingMips;
            IsReadable = isReadable;
            FilterMode = filterMode;
            Filtering = filtering;
            MipBias = mipBias;
            WrapModeU = wrapModeU;
            WrapModeV = wrapModeV;
            LightMapFormat = lightMapFormat;
            ColorSpace = colorSpace;
            LoadTexturePath = loadTexturePath;
        }
    }
}