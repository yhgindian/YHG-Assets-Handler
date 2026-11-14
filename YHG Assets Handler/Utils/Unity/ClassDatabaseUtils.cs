using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.Text;

namespace YHG_Assets_Handler.Utils.Unity
{
    public static class ClassDatabaseUtils
    {
        public static AssetTypeTemplateField CreateTemplateField(this ClassDatabaseFile cldb, int classid)
        {
            ClassDatabaseType cldbType = cldb.FindAssetClassByID(classid);
            var templateField = new AssetTypeTemplateField();
            templateField.FromClassDatabase(cldb, cldbType);
            return templateField;
        }

        public static AssetTypeValueField CreateEmptyBaseField(
            this ClassDatabaseFile cldb, int classid)
        {
            AssetTypeTemplateField tempField = cldb.CreateTemplateField(classid);
            return ValueBuilder.DefaultValueFieldFromTemplate(tempField);
        }
    }
}
