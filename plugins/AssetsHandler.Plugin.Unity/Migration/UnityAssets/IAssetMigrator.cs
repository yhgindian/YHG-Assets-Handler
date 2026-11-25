using AssetsTools.NET;

namespace AssetsHandler.Plugin.Unity.Migration.UnityAssets
{
    public interface IAssetMigrator
    {
        int TargetClassId { get; }
        AssetTypeValueField Migrate(AssetTypeValueField sourceField, ClassDatabaseFile targetDb, int targetClassId, string assetName, MigrationReport report);
    }
}