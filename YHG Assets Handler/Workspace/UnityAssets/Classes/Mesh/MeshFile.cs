using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using YHG_Assets_Handler.Utils.Unity;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public partial class MeshFile
    {
        public UnityVersion version;
        public string m_Name = "Mesh";
        public bool m_Use16BitIndices = true;
        public SubMesh[] m_SubMeshes = Array.Empty<SubMesh>();
        public uint[] m_IndexBuffer = Array.Empty<uint>();
        public int m_VertexCount;

        // Core Buffers
        public float[] m_Vertices = Array.Empty<float>();
        public float[] m_Normals = Array.Empty<float>();
        public float[] m_Colors = Array.Empty<float>();
        public float[] m_UV0 = Array.Empty<float>();
        public float[] m_UV1 = Array.Empty<float>();
        public float[] m_UV2 = Array.Empty<float>();
        public float[] m_UV3 = Array.Empty<float>();
        public float[] m_UV4 = Array.Empty<float>();
        public float[] m_UV5 = Array.Empty<float>();
        public float[] m_UV6 = Array.Empty<float>();
        public float[] m_UV7 = Array.Empty<float>();
        public float[] m_Tangents = Array.Empty<float>();

        // Complex Data
        public VertexData? m_VertexData;
        public CompressedMesh? m_CompressedMesh;
        public AABB? m_LocalAABB;
        public StreamingInfo? m_StreamData;
        public BlendShapeData? m_Shapes;

        // Bones & Rigging
        public BoneWeights4[] m_Skin = Array.Empty<BoneWeights4>();
        public Matrix4x4[] m_BindPose = Array.Empty<Matrix4x4>();
        public uint[] m_BoneNameHashes = Array.Empty<uint>();
        public uint m_RootBoneNameHash;
        public MinMaxAABB[] m_BonesAABB = Array.Empty<MinMaxAABB>();
        public uint[] m_VariableBoneCountWeights = Array.Empty<uint>();

        // Flags, Compression & Metrics
        public byte m_MeshCompression;
        public byte m_StreamCompression;
        public bool m_IsReadable = true;
        public bool m_KeepVertices;
        public bool m_KeepIndices;
        public int m_MeshUsageFlags;
        public int m_CookingOptions;
        public float[] m_MeshMetrics = new float[2];

        // Collision Cached Meshes
        public byte[] m_BakedConvexCollisionMesh = Array.Empty<byte>();
        public byte[] m_BakedTriangleCollisionMesh = Array.Empty<byte>();

        public List<uint> m_Indices = new();

        public MeshFile()
        {
            version = new UnityVersion("2021.3.45f2");
        }

        public MeshFile(UnityVersion version, AssetTypeValueField baseField)
        {
            this.version = version;
            var nameField = baseField["m_Name"];
            m_Name = !nameField.IsDummy ? nameField.AsString : "Mesh";

            // 1. 16-Bit Indices check
            if (version.major < 3 || (version.major == 3 && version.minor < 5))
            {
                var use16 = baseField["m_Use16BitIndices"];
                if (!use16.IsDummy)
                    m_Use16BitIndices = use16.AsInt > 0;
            }

            // 2. SubMeshes
            var subMeshesField = baseField["m_SubMeshes.Array"];
            if (subMeshesField.IsDummy) subMeshesField = baseField["m_SubMeshes"]?["Array"];

            if (subMeshesField != null && !subMeshesField.IsDummy)
            {
                int count = subMeshesField.AsArray.size;
                m_SubMeshes = new SubMesh[count];
                for (int i = 0; i < count; i++)
                {
                    m_SubMeshes[i] = new SubMesh(subMeshesField[i], version);
                }
            }

            // 3. BlendShapes (Unity 4.1+)
            if (version.major > 4 || (version.major == 4 && version.minor >= 1))
            {
                var shapesField = baseField["m_Shapes"];
                if (!shapesField.IsDummy)
                {
                    m_Shapes = new BlendShapeData(shapesField, version);
                }
            }

            // 4. BindPose & Bone Name Hashes (Unity 4.3+)
            if (version.major > 4 || (version.major == 4 && version.minor >= 3))
            {
                m_BindPose = ReadMatrixArray(baseField["m_BindPose.Array"]);

                var boneHashes = baseField["m_BoneNameHashes.Array"];
                if (!boneHashes.IsDummy)
                {
                    m_BoneNameHashes = new uint[boneHashes.AsArray.size];
                    for (int i = 0; i < m_BoneNameHashes.Length; i++)
                        m_BoneNameHashes[i] = boneHashes[i].AsUInt;
                }

                var rootHash = baseField["m_RootBoneNameHash"];
                if (!rootHash.IsDummy) m_RootBoneNameHash = rootHash.AsUInt;
            }

            // 5. BonesAABB & VariableBoneCountWeights (Unity 2019+)
            if (version.major >= 2019)
            {
                var bonesAABBField = baseField["m_BonesAABB.Array"];
                if (!bonesAABBField.IsDummy)
                {
                    m_BonesAABB = new MinMaxAABB[bonesAABBField.AsArray.size];
                    for (int i = 0; i < m_BonesAABB.Length; i++)
                        m_BonesAABB[i] = new MinMaxAABB(bonesAABBField[i]);
                }

                var varBones = baseField["m_VariableBoneCountWeights.Array"];
                if (!varBones.IsDummy)
                {
                    m_VariableBoneCountWeights = new uint[varBones.AsArray.size];
                    for (int i = 0; i < m_VariableBoneCountWeights.Length; i++)
                        m_VariableBoneCountWeights[i] = varBones[i].AsUInt;
                }
            }

            // 6. Mesh Compression & Flags (Unity 2.6+)
            if (version.major > 2 || (version.major == 2 && version.minor >= 6))
            {
                var comp = baseField["m_MeshCompression"];
                if (!comp.IsDummy) m_MeshCompression = (byte)comp.AsInt;

                if (version.major >= 4)
                {
                    if (version.major < 5)
                    {
                        var sComp = baseField["m_StreamCompression"];
                        if (!sComp.IsDummy) m_StreamCompression = (byte)sComp.AsInt;
                    }
                    var f = baseField["m_IsReadable"]; if (!f.IsDummy) m_IsReadable = f.AsBool;
                    f = baseField["m_KeepVertices"]; if (!f.IsDummy) m_KeepVertices = f.AsBool;
                    f = baseField["m_KeepIndices"]; if (!f.IsDummy) m_KeepIndices = f.AsBool;
                }

                var indexFormatField = baseField["m_IndexFormat"];
                if (!indexFormatField.IsDummy)
                {
                    m_Use16BitIndices = indexFormatField.AsInt == 0;
                }
            }

            // 7. Index Buffer Unpack
            byte[] rawIndices = AssetTypeFieldHelper.GetByteArraySafe(baseField, "m_IndexBuffer");
            if (rawIndices.Length > 0)
            {
                if (m_Use16BitIndices)
                {
                    m_IndexBuffer = new uint[rawIndices.Length / 2];
                    for (int i = 0; i < m_IndexBuffer.Length; i++)
                        m_IndexBuffer[i] = BitConverter.ToUInt16(rawIndices, i * 2);
                }
                else
                {
                    m_IndexBuffer = new uint[rawIndices.Length / 4];
                    for (int i = 0; i < m_IndexBuffer.Length; i++)
                        m_IndexBuffer[i] = BitConverter.ToUInt32(rawIndices, i * 4);
                }
            }

            // 8. Skin BoneWeights (< 2018.2)
            if (version.major < 2018 || (version.major == 2018 && version.minor < 2))
            {
                var skinArray = baseField["m_Skin.Array"];
                if (!skinArray.IsDummy)
                {
                    m_Skin = new BoneWeights4[skinArray.AsArray.size];
                    for (int i = 0; i < m_Skin.Length; i++)
                    {
                        m_Skin[i] = new BoneWeights4();
                        for (int j = 0; j < 4; j++)
                        {
                            var w = skinArray[i]?["weight"]?[j];
                            if (w != null && !w.IsDummy) m_Skin[i].weight[j] = w.AsFloat;

                            var b = skinArray[i]?["boneIndex"]?[j];
                            if (b != null && !b.IsDummy) m_Skin[i].boneIndex[j] = b.AsInt;
                        }
                    }
                }
            }

            // 9. Legacy Direct Vertex Arrays (< 3.5) vs VertexData (>= 3.5)
            var vDataField = baseField["m_VertexData"];
            if (vDataField != null && !vDataField.IsDummy)
            {
                m_VertexData = new VertexData(vDataField, version);
            }
            else if (version.major < 3 || (version.major == 3 && version.minor < 5))
            {
                var vcField = baseField["m_VertexCount"];
                if (!vcField.IsDummy) m_VertexCount = vcField.AsInt;

                m_Vertices = ReadFloatArrayFromField(baseField["m_Vertices.Array"]);
                m_Normals = ReadFloatArrayFromField(baseField["m_Normals.Array"]);
                m_UV0 = ReadFloatArrayFromField(baseField["m_UV.Array"]);
                m_Tangents = ReadFloatArrayFromField(baseField["m_Tangents.Array"]);

                if (version.major == 3 || (version.major == 4 && version.minor <= 2))
                {
                    m_BindPose = ReadMatrixArray(baseField["m_BindPose.Array"]);
                }
            }

            // 10. CompressedMesh (Unity 2.6+)
            var compField = baseField["m_CompressedMesh"];
            if (compField != null && !compField.IsDummy)
            {
                m_CompressedMesh = new CompressedMesh(compField, version);
            }

            // 11. Local AABB
            var aabbField = baseField["m_LocalAABB"];
            if (!aabbField.IsDummy)
            {
                m_LocalAABB = new AABB(aabbField);
            }

            // 12. Mesh Usage Flags
            var usageField = baseField["m_MeshUsageFlags"];
            if (!usageField.IsDummy) m_MeshUsageFlags = usageField.AsInt;

            // 13. Cooking Options (Unity 2022.1+)
            if (version.major > 2022 || (version.major == 2022 && version.minor >= 1))
            {
                var cook = baseField["m_CookingOptions"];
                if (!cook.IsDummy) m_CookingOptions = cook.AsInt;
            }

            // 14. Baked Collision Meshes (Unity 5.0+)
            if (version.major >= 5)
            {
                m_BakedConvexCollisionMesh = AssetTypeFieldHelper.GetByteArraySafe(baseField, "m_BakedConvexCollisionMesh");
                m_BakedTriangleCollisionMesh = AssetTypeFieldHelper.GetByteArraySafe(baseField, "m_BakedTriangleCollisionMesh");
            }

            // 15. Mesh Metrics (Unity 2018.2+)
            if (version.major > 2018 || (version.major == 2018 && version.minor >= 2))
            {
                var m0 = baseField["m_MeshMetrics[0]"];
                if (!m0.IsDummy) m_MeshMetrics[0] = m0.AsFloat;

                var m1 = baseField["m_MeshMetrics[1]"];
                if (!m1.IsDummy) m_MeshMetrics[1] = m1.AsFloat;
            }

            // 16. Streaming Data (Unity 2018.3+)
            var streamDataField = baseField["m_StreamData"];
            if (!streamDataField.IsDummy)
            {
                m_StreamData = new StreamingInfo(streamDataField);
            }

            ProcessData();
        }

        public void WriteTo(AssetTypeValueField baseField)
        {
            if (baseField.IsDummy) return;
            AssetTypeValueField tempField;

            // 1. Name
            if (!(tempField = baseField["m_Name"]).IsDummy)
                tempField.AsString = m_Name;

            // 2. Legacy 16-Bit Indices
            if (version.major < 3 || (version.major == 3 && version.minor < 5))
            {
                if (!(tempField = baseField["m_Use16BitIndices"]).IsDummy)
                    tempField.AsInt = m_Use16BitIndices ? 1 : 0;
            }

            // 3. Modern Index Format (2017.3+)
            if (!(tempField = baseField["m_IndexFormat"]).IsDummy)
                tempField.AsInt = m_Use16BitIndices ? 0 : 1;

            // 4. Index Buffer Serializer
            var idxBufField = baseField["m_IndexBuffer.Array"];
            if (idxBufField.IsDummy) idxBufField = baseField["m_IndexBuffer"];

            if (!idxBufField.IsDummy)
            {
                byte[] rawIndices;
                if (m_Use16BitIndices)
                {
                    rawIndices = new byte[m_IndexBuffer.Length * 2];
                    for (int i = 0; i < m_IndexBuffer.Length; i++)
                    {
                        byte[] b = BitConverter.GetBytes((ushort)m_IndexBuffer[i]);
                        rawIndices[i * 2] = b[0];
                        rawIndices[i * 2 + 1] = b[1];
                    }
                }
                else
                {
                    rawIndices = new byte[m_IndexBuffer.Length * 4];
                    for (int i = 0; i < m_IndexBuffer.Length; i++)
                    {
                        byte[] b = BitConverter.GetBytes(m_IndexBuffer[i]);
                        Buffer.BlockCopy(b, 0, rawIndices, i * 4, 4);
                    }
                }
                idxBufField.AsByteArray = rawIndices;
            }

            // 5. SubMeshes Serialization
            var subMeshesField = baseField["m_SubMeshes.Array"];
            if (subMeshesField.IsDummy) subMeshesField = baseField["m_SubMeshes"]?["Array"];

            if (subMeshesField != null && !subMeshesField.IsDummy)
            {
                subMeshesField.Children.Clear();
                foreach (var sm in m_SubMeshes)
                {
                    var slot = subMeshesField.GetEmptyArrayField() ?? ValueBuilder.DefaultValueFieldFromArrayTemplate(subMeshesField);
                    if (slot != null && !slot.IsDummy)
                    {
                        sm.WriteTo(slot, version);
                        subMeshesField.Children.Add(slot);
                    }
                }
            }

            // 6. BlendShapes
            if (m_Shapes != null && (version.major > 4 || (version.major == 4 && version.minor >= 1)))
            {
                var shapesField = baseField["m_Shapes"];
                if (!shapesField.IsDummy)
                    m_Shapes.WriteTo(shapesField, version);
            }

            // 7. Rigging & Bones
            if (version.major > 4 || (version.major == 4 && version.minor >= 3))
            {
                WriteMatrixArray(baseField["m_BindPose.Array"], m_BindPose);

                var boneHashes = baseField["m_BoneNameHashes.Array"];
                if (!boneHashes.IsDummy)
                {
                    boneHashes.Children.Clear();
                    foreach (var hash in m_BoneNameHashes)
                    {
                        var slot = boneHashes.GetEmptyArrayField() ?? ValueBuilder.DefaultValueFieldFromArrayTemplate(boneHashes);
                        if (slot != null && !slot.IsDummy)
                        {
                            slot.AsUInt = hash;
                            boneHashes.Children.Add(slot);
                        }
                    }
                }

                if (!(tempField = baseField["m_RootBoneNameHash"]).IsDummy)
                    tempField.AsUInt = m_RootBoneNameHash;
            }

            if (version.major >= 2019)
            {
                var bonesAABBField = baseField["m_BonesAABB.Array"];
                if (!bonesAABBField.IsDummy)
                {
                    bonesAABBField.Children.Clear();
                    foreach (var aabb in m_BonesAABB)
                    {
                        var slot = bonesAABBField.GetEmptyArrayField() ?? ValueBuilder.DefaultValueFieldFromArrayTemplate(bonesAABBField);
                        if (slot != null && !slot.IsDummy)
                        {
                            aabb.WriteTo(slot);
                            bonesAABBField.Children.Add(slot);
                        }
                    }
                }
            }

            // 8. VertexData vs Legacy Arrays
            var vDataField = baseField["m_VertexData"];
            if (vDataField != null && !vDataField.IsDummy && m_VertexData != null)
            {
                m_VertexData.WriteTo(vDataField, version);
            }
            else if (version.major < 3 || (version.major == 3 && version.minor < 5))
            {
                if (!(tempField = baseField["m_VertexCount"]).IsDummy)
                    tempField.AsInt = m_VertexCount;

                WriteFloatArrayToField(baseField["m_Vertices.Array"], m_Vertices);
                WriteFloatArrayToField(baseField["m_Normals.Array"], m_Normals);
                WriteFloatArrayToField(baseField["m_UV.Array"], m_UV0);
                WriteFloatArrayToField(baseField["m_Tangents.Array"], m_Tangents);
            }

            // 9. Fully Sanitize CompressedMesh (Force Unity to read from raw m_VertexData)
            if (!(tempField = baseField["m_MeshCompression"]).IsDummy)
                tempField.AsUInt = 0;

            var compField = baseField["m_CompressedMesh"];
            if (compField != null && !compField.IsDummy)
            {
                foreach (var child in compField.Children)
                {
                    if (child.TypeName == "PackedBitVector" && !child.IsDummy)
                    {
                        // Count zero karo
                        var numItems = child["m_NumItems"];
                        if (!numItems.IsDummy) numItems.AsUInt = 0;

                        // Range zero karo
                        var range = child["m_Range"];
                        if (!range.IsDummy) range.AsFloat = 0f;

                        // Start zero karo
                        var start = child["m_Start"];
                        if (!start.IsDummy) start.AsFloat = 0f;

                        // BitSize zero karo
                        var bitSize = child["m_BitSize"];
                        if (!bitSize.IsDummy) bitSize.AsUInt = 0;

                        // Safe extraction without null warnings
                        var dataArr = child["m_Data.Array"];
                        if (dataArr.IsDummy)
                        {
                            var mData = child["m_Data"];
                            dataArr = (!mData.IsDummy && !mData["Array"].IsDummy) ? mData["Array"] : mData;
                        }

                        if (!dataArr.IsDummy)
                        {
                            dataArr.Children?.Clear();
                            dataArr.AsByteArray = Array.Empty<byte>();
                        }
                    }
                }

                var uvInfo = compField["m_UVInfo"];
                if (!uvInfo.IsDummy) uvInfo.AsUInt = 0;
            }

            // 10. StreamData Reset
            var streamData = baseField["m_StreamData"];
            if (!streamData.IsDummy)
            {
                if (!(tempField = streamData["offset"]).IsDummy) tempField.AsULong = 0UL;
                if (!(tempField = streamData["size"]).IsDummy) tempField.AsUInt = 0U;
                if (!(tempField = streamData["path"]).IsDummy) tempField.AsString = string.Empty;
            }

            // 11. Recalculate and commit Local AABB & Mesh Metrics
            RecalculateBounds();
            var aabbField = baseField["m_LocalAABB"];
            if (!aabbField.IsDummy && m_LocalAABB != null)
            {
                m_LocalAABB.WriteTo(aabbField);
            }

            if (m_LocalAABB != null)
            {
                float radius = Math.Max(m_LocalAABB.m_Extent.X, Math.Max(m_LocalAABB.m_Extent.Y, m_LocalAABB.m_Extent.Z));
                if (!(tempField = baseField["m_MeshMetrics[0]"]).IsDummy) tempField.AsFloat = radius;
                if (!(tempField = baseField["m_MeshMetrics[1]"]).IsDummy) tempField.AsFloat = m_LocalAABB.m_Extent.X * m_LocalAABB.m_Extent.Y * m_LocalAABB.m_Extent.Z;
            }

            // 12. General Flags
            if (!(tempField = baseField["m_IsReadable"]).IsDummy) tempField.AsBool = m_IsReadable;
            if (!(tempField = baseField["m_KeepVertices"]).IsDummy) tempField.AsBool = m_KeepVertices;
            if (!(tempField = baseField["m_KeepIndices"]).IsDummy) tempField.AsBool = m_KeepIndices;
            if (!(tempField = baseField["m_MeshUsageFlags"]).IsDummy) tempField.AsInt = m_MeshUsageFlags;

            if (version.major > 2022 || (version.major == 2022 && version.minor >= 1))
            {
                if (!(tempField = baseField["m_CookingOptions"]).IsDummy)
                    tempField.AsInt = m_CookingOptions;
            }
        }

        private static Matrix4x4[] ReadMatrixArray(AssetTypeValueField? arrayField)
        {
            if (arrayField == null || arrayField.IsDummy) return Array.Empty<Matrix4x4>();
            int count = arrayField.AsArray.size;
            var result = new Matrix4x4[count];

            for (int i = 0; i < count; i++)
            {
                var m = arrayField[i];
                result[i] = new Matrix4x4(
                    m["e00"]?.AsFloat ?? 0f, m["e01"]?.AsFloat ?? 0f, m["e02"]?.AsFloat ?? 0f, m["e03"]?.AsFloat ?? 0f,
                    m["e10"]?.AsFloat ?? 0f, m["e11"]?.AsFloat ?? 0f, m["e12"]?.AsFloat ?? 0f, m["e13"]?.AsFloat ?? 0f,
                    m["e20"]?.AsFloat ?? 0f, m["e21"]?.AsFloat ?? 0f, m["e22"]?.AsFloat ?? 0f, m["e23"]?.AsFloat ?? 0f,
                    m["e30"]?.AsFloat ?? 0f, m["e31"]?.AsFloat ?? 0f, m["e32"]?.AsFloat ?? 0f, m["e33"]?.AsFloat ?? 0f
                );
            }
            return result;
        }

        private static void WriteMatrixArray(AssetTypeValueField? arrayField, Matrix4x4[] matrices)
        {
            if (arrayField == null || arrayField.IsDummy) return;

            arrayField.Children.Clear();
            foreach (var mat in matrices)
            {
                var slot = arrayField.GetEmptyArrayField() ?? ValueBuilder.DefaultValueFieldFromArrayTemplate(arrayField);
                if (slot != null && !slot.IsDummy)
                {
                    var f = slot["e00"]; if (!f.IsDummy) f.AsFloat = mat.M11;
                    f = slot["e01"]; if (!f.IsDummy) f.AsFloat = mat.M12;
                    f = slot["e02"]; if (!f.IsDummy) f.AsFloat = mat.M13;
                    f = slot["e03"]; if (!f.IsDummy) f.AsFloat = mat.M14;

                    f = slot["e10"]; if (!f.IsDummy) f.AsFloat = mat.M21;
                    f = slot["e11"]; if (!f.IsDummy) f.AsFloat = mat.M22;
                    f = slot["e12"]; if (!f.IsDummy) f.AsFloat = mat.M23;
                    f = slot["e13"]; if (!f.IsDummy) f.AsFloat = mat.M24;

                    f = slot["e20"]; if (!f.IsDummy) f.AsFloat = mat.M31;
                    f = slot["e21"]; if (!f.IsDummy) f.AsFloat = mat.M32;
                    f = slot["e22"]; if (!f.IsDummy) f.AsFloat = mat.M33;
                    f = slot["e23"]; if (!f.IsDummy) f.AsFloat = mat.M34;

                    f = slot["e30"]; if (!f.IsDummy) f.AsFloat = mat.M41;
                    f = slot["e31"]; if (!f.IsDummy) f.AsFloat = mat.M42;
                    f = slot["e32"]; if (!f.IsDummy) f.AsFloat = mat.M43;
                    f = slot["e33"]; if (!f.IsDummy) f.AsFloat = mat.M44;

                    arrayField.Children.Add(slot);
                }
            }
        }

        private static void WriteFloatArrayToField(AssetTypeValueField? arrayField, float[] values)
        {
            if (arrayField == null || arrayField.IsDummy) return;

            arrayField.Children.Clear();
            for (int i = 0; i < values.Length; i++)
            {
                var slot = arrayField.GetEmptyArrayField() ?? ValueBuilder.DefaultValueFieldFromArrayTemplate(arrayField);
                if (slot != null && !slot.IsDummy)
                {
                    slot.AsFloat = values[i];
                    arrayField.Children.Add(slot);
                }
            }
        }

        private static float[] ReadFloatArrayFromField(AssetTypeValueField? arrayField)
        {
            if (arrayField == null || arrayField.IsDummy) return Array.Empty<float>();
            int count = arrayField.AsArray.size;
            var res = new float[count];
            for (int i = 0; i < count; i++) res[i] = arrayField[i].AsFloat;
            return res;
        }

        private void ProcessData()
        {
            if (version.major > 3 || (version.major == 3 && version.minor >= 5))
            {
                ReadVertexData();
            }

            if (version.major > 2 || (version.major == 2 && version.minor >= 6))
            {
                DecompressCompressedMesh();
            }

            GetTriangles();
        }

        private void ReadVertexData()
        {
            if (m_VertexData == null) return;
            m_VertexCount = (int)m_VertexData.m_VertexCount;
            if (m_VertexCount == 0 || m_VertexData.m_Channels.Length == 0) return;

            for (int chn = 0; chn < m_VertexData.m_Channels.Length; chn++)
            {
                var ch = m_VertexData.m_Channels[chn];
                if (ch.dimension == 0 || ch.stream >= m_VertexData.m_Streams.Length) continue;

                var stream = m_VertexData.m_Streams[ch.stream];
                var channelMask = new BitArray(new[] { (int)stream.channelMask });
                if (!channelMask.Get(chn)) continue;

                if (version.major < 2018 && chn == 2 && ch.format == 2)
                    ch.dimension = 4;

                var vertexFormat = MeshHelper.ToVertexFormat(ch.format, version);
                var componentByteSize = (int)MeshHelper.GetFormatSize(vertexFormat);
                var componentBytes = new byte[m_VertexCount * ch.dimension * componentByteSize];

                for (int v = 0; v < m_VertexCount; v++)
                {
                    int vertexOffset = (int)stream.offset + ch.offset + (int)stream.stride * v;
                    for (int d = 0; d < ch.dimension; d++)
                    {
                        int componentOffset = vertexOffset + componentByteSize * d;
                        if (componentOffset + componentByteSize <= m_VertexData.m_DataSize.Length)
                        {
                            Buffer.BlockCopy(m_VertexData.m_DataSize, componentOffset, componentBytes, componentByteSize * (v * ch.dimension + d), componentByteSize);
                        }
                    }
                }

                int[]? componentsIntArray = null;
                float[]? componentsFloatArray = null;

                if (MeshHelper.IsIntFormat(vertexFormat))
                    componentsIntArray = MeshHelper.BytesToIntArray(componentBytes, vertexFormat);
                else
                    componentsFloatArray = MeshHelper.BytesToFloatArray(componentBytes, vertexFormat);

                if (version.major >= 2018)
                {
                    switch (chn)
                    {
                        case 0: m_Vertices = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 1: m_Normals = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 2: m_Tangents = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 3: m_Colors = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 4: m_UV0 = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 5: m_UV1 = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 6: m_UV2 = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 7: m_UV3 = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 8: m_UV4 = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 9: m_UV5 = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 10: m_UV6 = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 11: m_UV7 = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 12:
                            if (m_Skin.Length == 0) InitMSkin();
                            if (componentsFloatArray != null)
                            {
                                for (int i = 0; i < m_VertexCount; i++)
                                    for (int j = 0; j < ch.dimension; j++)
                                        m_Skin[i].weight[j] = componentsFloatArray[i * ch.dimension + j];
                            }
                            break;
                        case 13:
                            if (m_Skin.Length == 0) InitMSkin();
                            if (componentsIntArray != null)
                            {
                                for (int i = 0; i < m_VertexCount; i++)
                                    for (int j = 0; j < ch.dimension; j++)
                                        m_Skin[i].boneIndex[j] = componentsIntArray[i * ch.dimension + j];
                            }
                            break;
                    }
                }
                else
                {
                    switch (chn)
                    {
                        case 0: m_Vertices = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 1: m_Normals = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 2: m_Colors = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 3: m_UV0 = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 4: m_UV1 = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 5:
                            if (version.major >= 5) m_UV2 = componentsFloatArray ?? Array.Empty<float>();
                            else m_Tangents = componentsFloatArray ?? Array.Empty<float>();
                            break;
                        case 6: m_UV3 = componentsFloatArray ?? Array.Empty<float>(); break;
                        case 7: m_Tangents = componentsFloatArray ?? Array.Empty<float>(); break;
                    }
                }
            }
        }

        private void InitMSkin()
        {
            m_Skin = new BoneWeights4[m_VertexCount];
            for (int i = 0; i < m_VertexCount; i++)
                m_Skin[i] = new BoneWeights4();
        }

        private void DecompressCompressedMesh()
        {
            if (m_CompressedMesh == null) return;

            // Vertices
            if (m_CompressedMesh.m_Vertices?.m_NumItems > 0)
            {
                m_VertexCount = (int)m_CompressedMesh.m_Vertices.m_NumItems / 3;
                m_Vertices = m_CompressedMesh.m_Vertices.UnpackFloats(3, 3 * 4);
            }

            // Normals
            if (m_CompressedMesh.m_Normals?.m_NumItems > 0)
            {
                var normalData = m_CompressedMesh.m_Normals.UnpackFloats(2, 4 * 2);
                var signs = m_CompressedMesh.m_NormalSigns?.UnpackInts();
                m_Normals = new float[m_CompressedMesh.m_Normals.m_NumItems / 2 * 3];

                for (int i = 0; i < m_CompressedMesh.m_Normals.m_NumItems / 2; ++i)
                {
                    var x = normalData[i * 2 + 0];
                    var y = normalData[i * 2 + 1];
                    var zsqr = 1 - x * x - y * y;
                    float z = zsqr >= 0f ? (float)Math.Sqrt(zsqr) : 0f;

                    if (signs != null && signs.Length > i && signs[i] == 0) z = -z;

                    m_Normals[i * 3] = x;
                    m_Normals[i * 3 + 1] = y;
                    m_Normals[i * 3 + 2] = z;
                }
            }

            // Tangents
            if (m_CompressedMesh.m_Tangents?.m_NumItems > 0)
            {
                var tangentData = m_CompressedMesh.m_Tangents.UnpackFloats(2, 4 * 2);
                var signs = m_CompressedMesh.m_TangentSigns?.UnpackInts();
                m_Tangents = new float[m_CompressedMesh.m_Tangents.m_NumItems / 2 * 4];

                for (int i = 0; i < m_CompressedMesh.m_Tangents.m_NumItems / 2; ++i)
                {
                    var x = tangentData[i * 2 + 0];
                    var y = tangentData[i * 2 + 1];
                    var zsqr = 1 - x * x - y * y;
                    float z = zsqr >= 0f ? (float)Math.Sqrt(zsqr) : 0f;

                    if (signs != null && signs.Length > i * 2 && signs[i * 2 + 0] == 0) z = -z;
                    float w = (signs != null && signs.Length > i * 2 + 1 && signs[i * 2 + 1] > 0) ? 1.0f : -1.0f;

                    m_Tangents[i * 4] = x;
                    m_Tangents[i * 4 + 1] = y;
                    m_Tangents[i * 4 + 2] = z;
                    m_Tangents[i * 4 + 3] = w;
                }
            }

            // Skin Weights
            if (m_CompressedMesh.m_Weights?.m_NumItems > 0 && m_CompressedMesh.m_BoneIndices != null)
            {
                var weights = m_CompressedMesh.m_Weights.UnpackInts();
                var boneIndices = m_CompressedMesh.m_BoneIndices.UnpackInts();

                InitMSkin();

                int bonePos = 0, boneIndexPos = 0, j = 0, sum = 0;
                for (int i = 0; i < m_CompressedMesh.m_Weights.m_NumItems; i++)
                {
                    m_Skin[bonePos].weight[j] = weights[i] / 31.0f;
                    m_Skin[bonePos].boneIndex[j] = boneIndices[boneIndexPos++];
                    j++;
                    sum += weights[i];

                    if (sum >= 31)
                    {
                        for (; j < 4; j++)
                        {
                            m_Skin[bonePos].weight[j] = 0;
                            m_Skin[bonePos].boneIndex[j] = 0;
                        }
                        bonePos++;
                        j = 0;
                        sum = 0;
                    }
                    else if (j == 3)
                    {
                        m_Skin[bonePos].weight[j] = (31 - sum) / 31.0f;
                        m_Skin[bonePos].boneIndex[j] = boneIndices[boneIndexPos++];
                        bonePos++;
                        j = 0;
                        sum = 0;
                    }
                }
            }

            // Index Buffer
            if (m_CompressedMesh.m_Triangles?.m_NumItems > 0)
            {
                var unpacked = m_CompressedMesh.m_Triangles.UnpackInts();
                if (unpacked != null)
                {
                    m_IndexBuffer = Array.ConvertAll(unpacked, x => (uint)x);
                }
            }
        }

        private void GetTriangles()
        {
            m_Indices.Clear();
            if (m_IndexBuffer.Length == 0) return;

            foreach (var sm in m_SubMeshes)
            {
                var firstIndex = sm.firstByte / 2;
                if (!m_Use16BitIndices) firstIndex /= 2;

                var indexCount = sm.indexCount;
                var topology = sm.topology;

                if (topology == MeshHelper.GfxPrimitiveType.Triangles)
                {
                    for (int i = 0; i < indexCount; i += 3)
                    {
                        if (firstIndex + i + 2 >= m_IndexBuffer.Length) break;
                        m_Indices.Add(m_IndexBuffer[firstIndex + i]);
                        m_Indices.Add(m_IndexBuffer[firstIndex + i + 1]);
                        m_Indices.Add(m_IndexBuffer[firstIndex + i + 2]);
                    }
                }
                else if (version.major < 4 || topology == MeshHelper.GfxPrimitiveType.TriangleStrip)
                {
                    for (int i = 0; i < indexCount - 2; i++)
                    {
                        if (firstIndex + i + 2 >= m_IndexBuffer.Length) break;
                        var a = m_IndexBuffer[firstIndex + i];
                        var b = m_IndexBuffer[firstIndex + i + 1];
                        var c = m_IndexBuffer[firstIndex + i + 2];

                        if (a == b || a == c || b == c) continue;

                        if ((i & 1) == 1)
                        {
                            m_Indices.Add(b);
                            m_Indices.Add(a);
                        }
                        else
                        {
                            m_Indices.Add(a);
                            m_Indices.Add(b);
                        }
                        m_Indices.Add(c);
                    }
                }
            }
        }

        public void RecalculateBounds()
        {
            if (m_Vertices.Length < 3) return;

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            for (int i = 0; i < m_Vertices.Length; i += 3)
            {
                float x = m_Vertices[i];
                float y = m_Vertices[i + 1];
                float z = m_Vertices[i + 2];

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (z < minZ) minZ = z;

                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
                if (z > maxZ) maxZ = z;
            }

            m_LocalAABB = new AABB
            {
                m_Center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f),
                m_Extent = new Vector3((maxX - minX) * 0.5f, (maxY - minY) * 0.5f, (maxZ - minZ) * 0.5f)
            };
        }
    }
}