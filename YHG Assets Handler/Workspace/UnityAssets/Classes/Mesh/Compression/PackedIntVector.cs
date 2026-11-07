using AssetsTools.NET;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using YHG_Assets_Handler.Utils.Unity;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public class PackedIntVector
    {
        public uint m_NumItems;
        public byte[] m_Data = Array.Empty<byte>();
        public byte m_BitSize;

        public PackedIntVector() { }

        public PackedIntVector(AssetTypeValueField field)
        {
            if (field.IsDummy) return;

            var f = field["m_NumItems"];
            if (!f.IsDummy) m_NumItems = f.AsUInt;

            if (m_NumItems == 0) return;

            f = field["m_BitSize"];
            if (!f.IsDummy) m_BitSize = (byte)f.AsInt;

            m_Data = AssetTypeFieldHelper.GetByteArraySafe(field, "m_Data");
        }

        public void WriteTo(AssetTypeValueField field)
        {
            if (field.IsDummy) return;

            var f = field["m_NumItems"];
            if (!f.IsDummy) f.AsUInt = m_NumItems;

            f = field["m_BitSize"];
            if (!f.IsDummy) f.AsInt = m_BitSize;

            var dataField = field["m_Data.Array"];
            if (dataField.IsDummy) dataField = field["m_Data"];

            if (!dataField.IsDummy)
            {
                dataField.AsByteArray = m_Data;
            }
        }

        public int[] UnpackInts()
        {
            if (m_NumItems == 0 || m_BitSize == 0 || m_Data.Length == 0)
                return Array.Empty<int>();

            int bitPosition = 0;
            var bits = new BitArray(m_Data);
            var result = new int[m_NumItems];

            for (int i = 0; i < m_NumItems; i++)
            {
                int val = 0;
                for (int b = 0; b < m_BitSize; b++)
                {
                    if (bitPosition < bits.Length && bits.Get(bitPosition++))
                        val |= 1 << b;
                }
                result[i] = val;
            }
            return result;
        }

        public void PackInts(int[] values, byte bitSize)
        {
            m_NumItems = (uint)values.Length;
            m_BitSize = bitSize;

            if (m_NumItems == 0 || m_BitSize == 0)
            {
                m_Data = Array.Empty<byte>();
                return;
            }

            int totalBits = (int)(m_NumItems * m_BitSize);
            var bits = new BitArray(totalBits);
            int bitPosition = 0;

            for (int i = 0; i < values.Length; i++)
            {
                int val = values[i];
                for (int b = 0; b < m_BitSize; b++)
                {
                    bool bit = ((val >> b) & 1) != 0;
                    bits.Set(bitPosition++, bit);
                }
            }

            int byteCount = (totalBits + 7) / 8;
            m_Data = new byte[byteCount];
            bits.CopyTo(m_Data, 0);
        }
    }
}
