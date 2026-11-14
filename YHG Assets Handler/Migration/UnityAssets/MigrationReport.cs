namespace YHG_Assets_Handler.Migration.UnityAssets
{
    public class MigrationReport
    {
        public int ClassId { get; set; }
        public string AssetName { get; set; } = "Unknown";
        public List<string> MatchedFields { get; } = new();
        public List<string> MissingInTarget { get; } = new();
        public List<string> ExtraInTarget { get; } = new();
    }
}