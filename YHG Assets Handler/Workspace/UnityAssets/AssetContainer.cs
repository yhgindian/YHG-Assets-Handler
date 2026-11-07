using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace YHG_Assets_Handler.Workspace.UnityAssets
{
    public class AssetContainer : AssetFileInfo
    {
        public AssetsFileInstance FileInstance { get; private set; }
        public AssetsFileReader FileReader { get; private set; }
        public long AbsoluteByteStart { get; set; }
        public uint FinalSize { get; private set; }
        public string Container { get; set; } = string.Empty;
        public AssetTypeValueField? BaseValueField;
        public bool HasProperMonoFields { get; set; } = false;

        [MemberNotNullWhen(true, nameof(BaseValueField))]
        public bool HasValueField
        {
            get
            {
                if (BaseValueField == null)
                    return false;

                bool isMonoBehaviour = TypeId == 114 || TypeId < 0;
                if (isMonoBehaviour)
                    return HasProperMonoFields;

                return true;
            }
        }

        public AssetContainer(AssetFileInfo origInfo, AssetsFileInstance instance, AssetTypeValueField? baseField = null)
        {
            PathId = origInfo.PathId;
            ByteOffset = origInfo.ByteOffset;
            ByteSize = origInfo.ByteSize;
            TypeIdOrIndex = origInfo.TypeIdOrIndex;
            OldTypeId = origInfo.OldTypeId;
            ScriptTypeIndex = origInfo.ScriptTypeIndex;
            Stripped = origInfo.Stripped;
            TypeId = origInfo.TypeId;
            Replacer = origInfo.Replacer;

            FileInstance = instance;
            FileReader = instance.file.Reader;
            FinalSize = origInfo.ByteSize;
            AbsoluteByteStart = origInfo.GetAbsoluteByteOffset(instance.file);
            BaseValueField = baseField;
        }

        public AssetPPtr AssetPPtr => new AssetPPtr(FileInstance.path, PathId);

        public void UpdateData(AssetTypeValueField valueField)
        {
            UpdateData(valueField.WriteToByteArray(), valueField);
        }

        public void UpdateData(byte[] data, AssetTypeValueField? valueField = null)
        {
            base.SetNewData(data);
            BaseValueField = valueField;

            if (IsReplacerPreviewable && Replacer != null)
            {
                Stream previewStream = Replacer.GetPreviewStream();
                FileReader = new AssetsFileReader(previewStream);
                AbsoluteByteStart = 0;
                FinalSize = (uint)previewStream.Length;
            }
        }

        public void RefreshFromSavedFile(AssetFileInfo newInfo, AssetsFileInstance newInstance)
        {
            // 1. AssetFileInfo base fields update
            PathId = newInfo.PathId;
            ByteOffset = newInfo.ByteOffset;
            ByteSize = newInfo.ByteSize;
            TypeIdOrIndex = newInfo.TypeIdOrIndex;
            OldTypeId = newInfo.OldTypeId;
            ScriptTypeIndex = newInfo.ScriptTypeIndex;
            Stripped = newInfo.Stripped;
            TypeId = newInfo.TypeId;
            Replacer = null;

            // 2. Stream, Instance and Absolute Offsets re-sync
            FileInstance = newInstance;
            FileReader = newInstance.file.Reader;
            FinalSize = newInfo.ByteSize;
            AbsoluteByteStart = newInfo.GetAbsoluteByteOffset(newInstance.file);

            // Invalidate cached field to ensure fresh deserialization on subsequent reads
            BaseValueField = null;
        }

        public void RefreshFromSavedFile(AssetsFileInstance newInstance)
        {
            var newInfo = newInstance.file.GetAssetInfo(PathId);
            if (newInfo == null)
            {
                throw new System.InvalidOperationException($"Asset with PathID {PathId} not found in saved file: {newInstance.name}");
            }

            RefreshFromSavedFile(newInfo, newInstance);
        }
    }
}