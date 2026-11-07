using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.Text;

namespace YHG_Assets_Handler.Utils.Unity
{
    public static class AssetCreationHelper
    {
        public static AssetFileInfo? CreateAssetFileInfo(
            AssetsFile assetsFile,
            ClassDatabaseFile? classDatabase,
            long pathId,
            int typeId,
            ushort scriptIndex = 0xffff,
            bool preferEditor = false)
        {
            uint version = assetsFile.Header.Version;
            int typeIdOrIndex;
            ushort oldTypeId = 0;
            ushort scriptTypeIndex = version < 17 ? scriptIndex : (ushort)0;

            if (version >= 16)
            {
                int typeIndex = assetsFile.Metadata.FindTypeTreeTypeIndexByID(typeId, scriptIndex);
                if (typeIndex == -1)
                {
                    if (classDatabase == null)
                        return null;

                    TypeTreeType? newType;
                    if (assetsFile.Metadata.TypeTreeEnabled)
                    {
                        newType = ClassDatabaseToTypeTree.Convert(classDatabase, typeId, preferEditor);
                        if (newType == null)
                            return null;
                    }
                    else
                    {
                        newType = CreateDefaultTypeTree(typeId, scriptIndex);
                    }

                    typeIndex = assetsFile.Metadata.TypeTreeTypes.Count;
                    assetsFile.Metadata.TypeTreeTypes.Add(newType);
                }

                typeIdOrIndex = typeIndex;
            }
            else
            {
                typeIdOrIndex = typeId;
                oldTypeId = typeId < 0 ? (ushort)0x72 : (ushort)typeId;
            }

            return new AssetFileInfo
            {
                PathId = pathId,
                ByteOffset = -1,
                ByteSize = 0,
                TypeIdOrIndex = typeIdOrIndex,
                OldTypeId = oldTypeId,
                ScriptTypeIndex = scriptTypeIndex,
                Stripped = 0,
                TypeId = typeId,
                Replacer = null
            };
        }

        public static TypeTreeType CreateDefaultTypeTree(int typeId, ushort scriptIndex)
        {
            var blob = new TypeTreeBlob
            {
                Nodes = new List<TypeTreeNode>(),
                StringBufferBytes = Array.Empty<byte>()
            };

            return new TypeTreeType
            {
                TypeBlob = blob,
                TypeId = typeId,
                IsStrippedType = false,
                ScriptTypeIndex = scriptIndex,
                ScriptIdHash = Hash128.NewBlankHash(),
                TypeHash = Hash128.NewBlankHash(),
                TypeDependencies = Array.Empty<int>(),
                IsRefType = false,
                TypeReference = null
            };
        }
    }
}
