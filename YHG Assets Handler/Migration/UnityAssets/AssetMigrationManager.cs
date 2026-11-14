using System;
using System.Collections.Generic;
using System.Linq;
using AssetsTools.NET;

namespace YHG_Assets_Handler.Migration.UnityAssets
{
    public class AssetMigrationManager
    {
        private readonly Dictionary<int, IAssetMigrator> _migrators = new();
        private readonly IAssetMigrator _defaultMigrator = new DefaultAssetMigrator();

        public AssetMigrationManager()
        {
            //Register(new TransformMigrator());
        }

        private void Register(IAssetMigrator migrator)
        {
            _migrators[migrator.TargetClassId] = migrator;
        }

        public (AssetTypeValueField ResultField, MigrationReport Report) ProcessMigration(
            AssetTypeValueField sourceField,
            int classId,
            ClassDatabaseFile targetDb,
            string assetName = "UnknownAsset")
        {
            var report = new MigrationReport();
            IAssetMigrator migrator;

            if (_migrators.ContainsKey(classId))
            {
                migrator = _migrators[classId];
            }
            else
            {
                migrator = _defaultMigrator;
            }

            var resultField = migrator.Migrate(sourceField, targetDb, classId, assetName, report);

            return (resultField, report);
        }
    }
}