using AssetsTools.NET;

namespace YHG_Assets_Handler.Migration.UnityAssets
{
    public interface IAssetMigrator
    {
        int TargetClassId { get; }
        AssetTypeValueField Migrate(AssetTypeValueField sourceField, ClassDatabaseFile targetDb, int targetClassId, string assetName, MigrationReport report);
    }
}