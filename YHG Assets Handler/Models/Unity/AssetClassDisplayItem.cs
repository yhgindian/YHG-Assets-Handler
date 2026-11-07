using AssetsTools.NET.Extra;

namespace YHG_Assets_Handler.Models.Unity
{
    public class AssetClassDisplayItem
    {
        public AssetClassID ClassId { get; set; }
        public int Id => (int)ClassId;
        public string Name { get; set; } = string.Empty;
        public string DisplayName => $"{Name} ({Id})";

        public override string ToString() => DisplayName;
    }
}