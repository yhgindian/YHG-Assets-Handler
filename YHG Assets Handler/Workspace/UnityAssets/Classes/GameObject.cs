using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using YHG_Assets_Handler.Utils.Unity;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes
{
    public class GameObject
    {
        public PPtr[] m_Components = Array.Empty<PPtr>();
        public string? m_Name;
        public int m_Layer;
        public ushort m_Tag;
        public bool m_IsActive;
        public GameObject() { }
        public GameObject(AssetTypeValueField field)
        {
            Read(field);
        }
        public void Read(AssetTypeValueField field)
        {
            if (field == null || field.IsDummy) return;
            var compArray = field["m_Component.Array"];
            if (compArray.IsDummy)
                compArray = field["m_Component"]?["Array"];
            if (compArray != null && !compArray.IsDummy && compArray.Children != null)
            {
                int size = compArray.Children.Count;
                m_Components = new PPtr[size];
                for (int i = 0; i < size; i++)
                {
                    var pairField = compArray[i];
                    var compField = pairField["component"];
                    if (compField.IsDummy)
                        compField = pairField;
                    m_Components[i] = new PPtr(compField);
                }
            }
            var nameField = field["m_Name"];
            if (!nameField.IsDummy)
                m_Name = nameField.AsString;
            var layerField = field["m_Layer"];
            if (!layerField.IsDummy)
                m_Layer = layerField.AsInt;
            var tagField = field["m_Tag"];
            if (!tagField.IsDummy)
                m_Tag = (ushort)tagField.AsUShort;
            var activeField = field["m_IsActive"];
            if (!activeField.IsDummy)
                m_IsActive = activeField.AsBool;
        }
        public void Write(AssetTypeValueField field, int serializedVersion = 22)
        {
            if (field == null || field.IsDummy) return;
            var nameField = field["m_Name"];
            if (!nameField.IsDummy && m_Name != null)
                nameField.AsString = m_Name;
            var layerField = field["m_Layer"];
            if (!layerField.IsDummy)
                layerField.AsInt = m_Layer;
            var tagField = field["m_Tag"];
            if (!tagField.IsDummy)
                tagField.AsUShort = m_Tag;
            var activeField = field["m_IsActive"];
            if (!activeField.IsDummy)
                activeField.AsBool = m_IsActive;
            var compArray = field["m_Component.Array"];
            if (compArray.IsDummy)
                compArray = field["m_Component"]?["Array"];
            if (compArray == null || compArray.IsDummy || m_Components == null) return;
            compArray.Children.Clear();
            for (int i = 0; i < m_Components.Length; i++)
            {
                var targetComp = m_Components[i];
                if (targetComp == null) continue;
                var pairField = compArray.GetEmptyArrayField()
                                ?? ValueBuilder.DefaultValueFieldFromArrayTemplate(compArray);
                if (pairField == null || pairField.IsDummy)
                    continue;
                var compField = pairField["component"];
                if (compField.IsDummy)
                    compField = pairField;
                targetComp.WriteTo(compField, serializedVersion);
                compArray.Children.Add(pairField);
            }
        }
    }
}