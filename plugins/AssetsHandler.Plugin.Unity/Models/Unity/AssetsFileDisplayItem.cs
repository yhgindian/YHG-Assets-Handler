using AssetsTools.NET.Extra;

namespace AssetsHandler.Plugin.Unity.Models.Unity
{
    public class AssetsFileDisplayItem
    {
        public string Name { get; set; } = string.Empty;
        public AssetsFileInstance Instance { get; set; } = null!;
        public AssetsFileDisplayItem(AssetsFileInstance instance)
        {
            Instance = instance;
            Name = instance.name;
        }

        public AssetsFileDisplayItem() { }
        public override string ToString() => Name;
    }
}