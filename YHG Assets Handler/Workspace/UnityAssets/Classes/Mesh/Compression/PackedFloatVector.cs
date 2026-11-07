using AssetsTools.NET;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using YHG_Assets_Handler.Utils.Unity;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public class PackedFloatVector
    {
        public uint m_NumItems;
        public float m_Range;
        public float m_Start;
        public byte[] m_Data = Array.Empty<byte>();
        public byte m_BitSize;

        public PackedFloatVector() { }

        public PackedFloatVector(AssetTypeValueField field)
        {
            if (field.IsDummy) return;

            var f = field["m_NumItems"];
            if (!f.IsDummy) m_NumItems = f.AsUInt;

            if (m_NumItems == 0) return;

            f = field["m_Range"];
            if (!f.IsDummy) m_Range = f.AsFloat;

            f = field["m_Start"];
            if (!f.IsDummy) m_Start = f.AsFloat;

            f = field["m_BitSize"];
            if (!f.IsDummy) m_BitSize = (byte)f.AsInt;

            m_Data = AssetTypeFieldHelper.GetByteArraySafe(field, "m_Data");
        }

        public void WriteTo(AssetTypeValueField field)
        {
            if (field.IsDummy) return;

            var f = field["m_NumItems"];
            if (!f.IsDummy) f.AsUInt = m_NumItems;

            f = field["m_Range"];
            if (!f.IsDummy) f.AsFloat = m_Range;

            f = field["m_Start"];
            if (!f.IsDummy) f.AsFloat = m_Start;

            f = field["m_BitSize"];
            if (!f.IsDummy) f.AsInt = m_BitSize;

            var dataField = field["m_Data.Array"];
            if (dataField.IsDummy) dataField = field["m_Data"];

            if (!dataField.IsDummy)
            {
                dataField.AsByteArray = m_Data;
            }
        }

        public float[] UnpackFloats(int itemCountInGroup, int groupStride, int startOffset = 0, int numVertices = 0)
        {
            if (m_NumItems == 0 || m_BitSize == 0 || m_Data.Length == 0)
                return Array.Empty<float>();

            int bitPosition = startOffset * m_BitSize;
            var bits = new BitArray(m_Data);
            int count = numVertices > 0 ? numVertices * itemCountInGroup : (int)m_NumItems;
            var result = new float[count];

            uint maxVal = (1u << m_BitSize) - 1u;
            float scale = maxVal > 0 ? m_Range / maxVal : 0f;

            for (int i = 0; i < count; i++)
            {
                uint val = 0;
                for (int b = 0; b < m_BitSize; b++)
                {
                    if (bitPosition < bits.Length && bits.Get(bitPosition++))
                        val |= 1u << b;
                }

                result[i] = m_Start + val * scale;
            }

            return result;
        }

        public void PackFloats(float[] values, byte bitSize)
        {
            m_NumItems = (uint)values.Length;
            m_BitSize = bitSize;

            if (m_NumItems == 0 || m_BitSize == 0)
            {
                m_Range = 0f;
                m_Start = 0f;
                m_Data = Array.Empty<byte>();
                return;
            }

            float min = values[0];
            float max = values[0];
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] < min) min = values[i];
                if (values[i] > max) max = values[i];
            }

            m_Start = min;
            m_Range = max - min;

            uint maxVal = (1u << m_BitSize) - 1u;
            float invRange = m_Range > 0.0000001f ? maxVal / m_Range : 0f;

            int totalBits = (int)(m_NumItems * m_BitSize);
            var bits = new BitArray(totalBits);
            int bitPosition = 0;

            for (int i = 0; i < values.Length; i++)
            {
                uint quantVal = (uint)Math.Clamp((int)Math.Round((values[i] - m_Start) * invRange), 0, (int)maxVal);

                for (int b = 0; b < m_BitSize; b++)
                {
                    bool bit = ((quantVal >> b) & 1) != 0;
                    bits.Set(bitPosition++, bit);
                }
            }

            int byteCount = (totalBits + 7) / 8;
            m_Data = new byte[byteCount];
            bits.CopyTo(m_Data, 0);
        }
    }
}
