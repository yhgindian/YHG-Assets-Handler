using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.Linq;
using YHG_Assets_Handler.Utils.Unity;

namespace YHG_Assets_Handler.Migration.UnityAssets
{
    public class DefaultAssetMigrator : IAssetMigrator
    {
        // -1 = for all assets
        public int TargetClassId => -1;

        public AssetTypeValueField Migrate(AssetTypeValueField sourceField, ClassDatabaseFile targetDb, int targetClassId, string assetName, MigrationReport report)
        {
            report.ClassId = targetClassId;
            report.AssetName = assetName;

            var targetBaseField = targetDb.CreateEmptyBaseField(targetClassId);

            CopyAndAuditFields(sourceField, targetBaseField, report, parentPath: assetName);

            return targetBaseField;
        }

        protected void CopyAndAuditFields(AssetTypeValueField src, AssetTypeValueField tgt, MigrationReport report, string parentPath)
        {
            if (src == null || tgt == null) return;

            bool isSrcArray = src.TemplateField != null && src.TemplateField.IsArray;
            bool isTgtArray = tgt.TemplateField != null && tgt.TemplateField.IsArray;

            if (isSrcArray && isTgtArray)
            {
                if (src.TemplateField?.ValueType == AssetValueType.ByteArray && tgt.TemplateField?.ValueType == AssetValueType.ByteArray)
                {
                    tgt.Value = src.Value?.Clone();
                    report.MatchedFields.Add(parentPath);
                    return;
                }

                // Arrays (Target template: Children[0] = size, Children[1] = element template)
                if (tgt.TemplateField?.Children != null && tgt.TemplateField.Children.Count >= 2)
                {
                    var elemTemplate = tgt.TemplateField.Children[1];
                    var srcChildren = src.Children ?? new List<AssetTypeValueField>();

                    tgt.Children = new List<AssetTypeValueField>(srcChildren.Count);

                    for (int i = 0; i < srcChildren.Count; i++)
                    {
                        string currentPath = $"{parentPath}[{i}]";
                        var srcElem = srcChildren[i];

                        var tgtElem = ValueBuilder.DefaultValueFieldFromTemplate(elemTemplate);

                        if ((srcElem.Children != null && srcElem.Children.Count > 0) || srcElem.TemplateField?.ValueType == AssetValueType.None)
                        {
                            CopyAndAuditFields(srcElem, tgtElem, report, currentPath);
                        }
                        else
                        {
                            tgtElem.Value = srcElem.Value?.Clone();
                            report.MatchedFields.Add(currentPath);
                        }

                        tgt.Children.Add(tgtElem);
                    }

                    if (tgt.Value != null && tgt.Value.ValueType == AssetValueType.Array)
                    {
                        tgt.Value.AsArray = new AssetTypeArrayInfo { size = tgt.Children.Count };
                    }
                    else
                    {
                        tgt.Value = new AssetTypeValue(AssetValueType.Array, new AssetTypeArrayInfo { size = tgt.Children.Count });
                    }
                }
                return;
            }

            if (src.TemplateField?.ValueType == AssetValueType.String && tgt.TemplateField?.ValueType == AssetValueType.String)
            {
                tgt.Value = src.Value?.Clone();
                report.MatchedFields.Add(parentPath);
                return;
            }

            var srcChildrenList = src.Children ?? new List<AssetTypeValueField>();
            var tgtChildrenList = tgt.Children ?? new List<AssetTypeValueField>();

            foreach (var tgtChild in tgtChildrenList)
            {
                string fieldName = tgtChild.FieldName;
                string currentPath = $"{parentPath}.{fieldName}";
                var srcChild = srcChildrenList.FirstOrDefault(c => c.FieldName == fieldName);

                if (srcChild != null)
                {
                    report.MatchedFields.Add(currentPath);

                    if ((srcChild.Children != null && srcChild.Children.Count > 0) || (tgtChild.Children != null && tgtChild.Children.Count > 0))
                    {
                        CopyAndAuditFields(srcChild, tgtChild, report, currentPath);
                    }
                    else
                    {
                        tgtChild.Value = srcChild.Value?.Clone();
                    }
                }
                else
                {
                    report.MissingInTarget.Add(currentPath);
                }
            }

            foreach (var srcChild in srcChildrenList)
            {
                string fieldName = srcChild.FieldName;
                string currentPath = $"{parentPath}.{fieldName}";
                if (!tgtChildrenList.Any(t => t.FieldName == fieldName))
                {
                    report.ExtraInTarget.Add(currentPath);
                }
            }
        }
    }
}