using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.Text;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public static class MeshHelper
    {
        public enum VertexChannelFormat { Float, Float16, Color, Byte, UInt32 }

        public enum VertexFormat
        {
            Float, Float16, UNorm8, SNorm8, UNorm16, SNorm16,
            UInt8, SInt8, UInt16, SInt16, UInt32, SInt32
        }

        public enum GfxPrimitiveType
        {
            Triangles = 0,
            TriangleStrip = 1,
            Quads = 2,
            Lines = 3,
            LineStrip = 4,
            Points = 5
        }

        public static VertexFormat ToVertexFormat(int format, UnityVersion version)
        {
            if (version.major < 2017)
            {
                return (VertexChannelFormat)format switch
                {
                    VertexChannelFormat.Float => VertexFormat.Float,
                    VertexChannelFormat.Float16 => VertexFormat.Float16,
                    VertexChannelFormat.Color => VertexFormat.UNorm8,
                    VertexChannelFormat.Byte => VertexFormat.UInt8,
                    VertexChannelFormat.UInt32 => VertexFormat.UInt32,
                    _ => VertexFormat.Float
                };
            }
            if (version.major < 2019)
            {
                return format switch
                {
                    0 => VertexFormat.Float,
                    1 => VertexFormat.Float16,
                    2 or 3 => VertexFormat.UNorm8,
                    4 => VertexFormat.SNorm8,
                    5 => VertexFormat.UNorm16,
                    6 => VertexFormat.SNorm16,
                    7 => VertexFormat.UInt8,
                    8 => VertexFormat.SInt8,
                    9 => VertexFormat.UInt16,
                    10 => VertexFormat.SInt16,
                    11 => VertexFormat.UInt32,
                    12 => VertexFormat.SInt32,
                    _ => VertexFormat.Float
                };
            }
            return (VertexFormat)format;
        }

        public static uint GetFormatSize(VertexFormat format) => format switch
        {
            VertexFormat.Float or VertexFormat.UInt32 or VertexFormat.SInt32 => 4u,
            VertexFormat.Float16 or VertexFormat.UNorm16 or VertexFormat.SNorm16 or VertexFormat.UInt16 or VertexFormat.SInt16 => 2u,
            VertexFormat.UNorm8 or VertexFormat.SNorm8 or VertexFormat.UInt8 or VertexFormat.SInt8 => 1u,
            _ => 4u
        };

        public static bool IsIntFormat(VertexFormat format) => format >= VertexFormat.UInt8;

        public static float[] BytesToFloatArray(byte[] inputBytes, VertexFormat format)
        {
            var size = (int)GetFormatSize(format);
            var len = inputBytes.Length / size;
            var result = new float[len];

            for (int i = 0; i < len; i++)
            {
                int offset = i * size;
                result[i] = format switch
                {
                    VertexFormat.Float => BitConverter.ToSingle(inputBytes, offset),
                    VertexFormat.Float16 => (float)BitConverter.ToHalf(inputBytes, offset),
                    VertexFormat.UNorm8 => inputBytes[offset] / 255f,
                    VertexFormat.SNorm8 => Math.Max((sbyte)inputBytes[offset] / 127f, -1f),
                    VertexFormat.UNorm16 => BitConverter.ToUInt16(inputBytes, offset) / 65535f,
                    VertexFormat.SNorm16 => Math.Max(BitConverter.ToInt16(inputBytes, offset) / 32767f, -1f),
                    _ => 0f
                };
            }
            return result;
        }

        public static int[] BytesToIntArray(byte[] inputBytes, VertexFormat format)
        {
            var size = (int)GetFormatSize(format);
            var len = inputBytes.Length / size;
            var result = new int[len];

            for (int i = 0; i < len; i++)
            {
                int offset = i * size;
                result[i] = format switch
                {
                    VertexFormat.UInt8 or VertexFormat.SInt8 => inputBytes[offset],
                    VertexFormat.UInt16 => BitConverter.ToUInt16(inputBytes, offset),
                    VertexFormat.SInt16 => BitConverter.ToInt16(inputBytes, offset),
                    VertexFormat.UInt32 or VertexFormat.SInt32 => BitConverter.ToInt32(inputBytes, offset),
                    _ => 0
                };
            }
            return result;
        }
    }
}
