using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes
{
    public class PPtr
    {
        public int m_FileID;
        public long m_PathID;
        public PPtr()
        {
            m_FileID = 0;
            m_PathID = 0;
        }
        public PPtr(int fileID, long pathID)
        {
            m_FileID = fileID;
            m_PathID = pathID;
        }
        public PPtr(AssetTypeValueField pptrField, int? serializedVersion = 22)
        {
            if (pptrField == null || pptrField.IsDummy)
                return;
            var fileIdField = pptrField["m_FileID"];
            if (!fileIdField.IsDummy)
                m_FileID = fileIdField.AsInt;
            var pathIdField = pptrField["m_PathID"];
            if (!pathIdField.IsDummy)
            {
                if (serializedVersion.HasValue && serializedVersion.Value < 14)
                    m_PathID = pathIdField.AsInt;
                else
                    m_PathID = pathIdField.AsLong;
            }
        }
        public void WriteTo(AssetTypeValueField pptrField, int serializedVersion = 22)
        {
            if (pptrField == null || pptrField.IsDummy)
                return;
            var fileIdField = pptrField["m_FileID"];
            if (!fileIdField.IsDummy)
                fileIdField.AsInt = m_FileID;
            var pathIdField = pptrField["m_PathID"];
            if (!pathIdField.IsDummy)
            {
                if (pathIdField.Value != null && pathIdField.Value.ValueType == AssetValueType.Int32 || (serializedVersion > 0 && serializedVersion < 14))
                {
                    pathIdField.AsInt = (int)m_PathID;
                }
                else
                {
                    pathIdField.AsLong = m_PathID;
                }
            }
        }
        public bool IsNull => m_PathID == 0;
        public override string ToString()
        {
            return $"PPtr(FileID: {m_FileID}, PathID: {m_PathID})";
        }
    }
}