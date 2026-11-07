using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Linq;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Font
{
    public static class FontHelper
    {
        public static AssetTypeValueField? GetByteArrayFont(UnityAssetWorkspace workspace, AssetContainer container)
        {
            if (container?.FileInstance == null)
                return null;

            AssetTypeTemplateField? fontTemp = workspace.GetTemplateField(container);
            if (fontTemp == null)
                return null;

            AssetTypeTemplateField? fontData = fontTemp.Children.FirstOrDefault(f => f.Name == "m_FontData");
            if (fontData == null || fontData.Children.Count == 0)
                return null;

            fontData.Children[0].ValueType = AssetValueType.ByteArray;

            AssetTypeValueField baseField;
            lock (container.FileInstance.LockReader)
            {
                baseField = fontTemp.MakeValue(container.FileReader, container.AbsoluteByteStart);
            }

            return baseField;
        }

        public static bool IsDataOtf(byte[] byteData)
        {
            if (byteData == null || byteData.Length < 4)
                return false;

            return byteData[0] == 0x4F &&
                   byteData[1] == 0x54 &&
                   byteData[2] == 0x54 &&
                   byteData[3] == 0x4F;
        }
    }
}