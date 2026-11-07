using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.Text;

namespace YHG_Assets_Handler.Utils.Unity
{
    public static class AssetTypeFieldHelper
    {
        public static AssetTypeValueField GetEmptyArrayField(this AssetTypeValueField arrayField)
        {
            return ValueBuilder.DefaultValueFieldFromArrayTemplate(arrayField);
        }

        public static byte[] GetByteArraySafe(this AssetTypeValueField? parent, string fieldName)
        {
            if (parent == null || parent.IsDummy) return Array.Empty<byte>();

            // 1. Check fieldName.Array
            var target = parent[$"{fieldName}.Array"];

            // 2. Fallback to direct field or nested Array child
            if (target.IsDummy)
            {
                target = parent[fieldName];
                if (!target.IsDummy && !target["Array"].IsDummy)
                {
                    target = target["Array"];
                }
            }

            if (target.IsDummy) return Array.Empty<byte>();

            // 3. Direct byte array value read
            if (target.Value != null)
            {
                try
                {
                    return target.AsByteArray;
                }
                catch
                {
                    // Fall through to children iteration
                }
            }

            // 4. Fallback: Array of Byte/UInt8 children
            if (target.Children.Count > 0)
            {
                byte[] bytes = new byte[target.Children.Count];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = (byte)target.Children[i].AsInt;
                }
                return bytes;
            }

            return Array.Empty<byte>();
        }
    }
}
