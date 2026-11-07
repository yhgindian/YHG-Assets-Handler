using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public partial class MeshFile
    {
        #region Rebuild Engine & Geometry Generators

        /// <summary>
        /// Automatically reconstructs low-level binary buffers (m_VertexData, m_Channels, m_Streams, m_IndexBuffer, LocalAABB, MeshMetrics)
        /// directly from high-level arrays (m_Vertices, m_Normals, m_UV0..7, m_Indices / m_SubMeshes).
        /// </summary>
        public void RebuildBuffers(bool autoGenerateNormalsIfMissing = true, bool autoGenerateTangentsIfMissing = true)
        {
            if (m_Vertices.Length == 0)
                throw new InvalidOperationException("Cannot rebuild mesh with empty vertices.");

            m_VertexCount = m_Vertices.Length / 3;

            // 1. Auto generate Normals if missing
            if (autoGenerateNormalsIfMissing && (m_Normals.Length != m_VertexCount * 3))
            {
                GenerateNormals();
            }

            // 2. Auto generate Tangents if missing & UV0 is present
            if (autoGenerateTangentsIfMissing && (m_Tangents.Length != m_VertexCount * 4) && (m_UV0.Length == m_VertexCount * 2))
            {
                GenerateTangents();
            }

            // 3. Auto-determine 16-bit vs 32-bit Index Format
            m_Use16BitIndices = m_VertexCount <= 65535;

            // 4. Index Buffer Pack & Submesh Resolution
            RebuildIndexBufferAndSubMeshes();

            // 5. Interleaved VertexData Generator (Unity >= 3.5)
            if (version.major > 3 || (version.major == 3 && version.minor >= 5))
            {
                GenerateInterleavedVertexData();
            }

            // 6. Recalculate Global Bounds & Submesh AABBs
            RecalculateBounds();

            if (m_LocalAABB != null && m_SubMeshes.Length > 0)
            {
                foreach (var sm in m_SubMeshes)
                {
                    if (sm.localAABB == null)
                        sm.localAABB = m_LocalAABB;
                }
            }

            // 7. Sanitize StreamData & CompressedMesh (Enforce embedded buffer load)
            m_StreamData = new StreamingInfo { offset = 0, size = 0, path = string.Empty };
            m_CompressedMesh = null;
            m_MeshCompression = 0;
            m_IsReadable = true;
        }

        private void RebuildIndexBufferAndSubMeshes()
        {
            // If user supplied m_Indices but didn't build SubMeshes, wrap into single submesh
            if (m_Indices.Count > 0 && (m_SubMeshes == null || m_SubMeshes.Length == 0))
            {
                m_IndexBuffer = m_Indices.ToArray();
                m_SubMeshes = new SubMesh[]
                {
                    new SubMesh
                    {
                        firstByte = 0,
                        indexCount = (uint)m_Indices.Count,
                        topology = MeshHelper.GfxPrimitiveType.Triangles,
                        triangleCount = (uint)(m_Indices.Count / 3),
                        baseVertex = 0,
                        firstVertex = 0,
                        vertexCount = (uint)m_VertexCount
                    }
                };
            }
            else if (m_SubMeshes.Length > 0 && m_Indices.Count == 0)
            {
                // Sync m_Indices from m_IndexBuffer
                m_Indices = m_IndexBuffer.ToList();
            }

            // Pack index bytes
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
        }

        private void GenerateInterleavedVertexData()
        {
            bool hasNormals = m_Normals.Length == m_VertexCount * 3;
            bool hasTangents = m_Tangents.Length == m_VertexCount * 4;
            bool hasColors = m_Colors.Length == m_VertexCount * 4;
            bool hasUV0 = m_UV0.Length == m_VertexCount * 2;
            bool hasUV1 = m_UV1.Length == m_VertexCount * 2;

            int totalChannelSlots = version.major >= 2018 ? 14 : (version.major >= 5 ? 8 : 6);
            var channels = new ChannelInfo[totalChannelSlots];
            for (int i = 0; i < totalChannelSlots; i++)
                channels[i] = new ChannelInfo { stream = 0, offset = 0, format = 0, dimension = 0 };

            byte currentOffset = 0;

            void AssignChannel(int chnIdx, byte dimension, byte format)
            {
                channels[chnIdx].stream = 0;
                channels[chnIdx].offset = currentOffset;
                channels[chnIdx].dimension = dimension;
                channels[chnIdx].format = format;
                currentOffset += (byte)(dimension * 4); // Float32 = 4 bytes
            }

            // 0: Position (Always Float3)
            AssignChannel(0, 3, 0);

            // 1: Normal
            if (hasNormals)
                AssignChannel(1, 3, 0);

            // Tangents, Colors, UVs mapping
            if (version.major >= 2018)
            {
                if (hasTangents) AssignChannel(2, 4, 0);
                if (hasColors) AssignChannel(3, 4, 0);
                if (hasUV0) AssignChannel(4, 2, 0);
                if (hasUV1) AssignChannel(5, 2, 0);
            }
            else
            {
                if (hasColors) AssignChannel(2, 4, 2); // UNorm8 / Color in older Unity
                if (hasUV0) AssignChannel(3, 2, 0);
                if (hasUV1) AssignChannel(4, 2, 0);
                if (hasTangents)
                {
                    if (version.major >= 5) AssignChannel(7, 4, 0);
                    else AssignChannel(5, 4, 0);
                }
            }

            uint stride = currentOffset;
            byte[] interleavedData = new byte[m_VertexCount * stride];

            for (int v = 0; v < m_VertexCount; v++)
            {
                int vertexStart = (int)(v * stride);

                // Position (X, Y, Z)
                Buffer.BlockCopy(BitConverter.GetBytes(m_Vertices[v * 3 + 0]), 0, interleavedData, vertexStart + channels[0].offset + 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(m_Vertices[v * 3 + 1]), 0, interleavedData, vertexStart + channels[0].offset + 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(m_Vertices[v * 3 + 2]), 0, interleavedData, vertexStart + channels[0].offset + 8, 4);

                // Normals
                if (hasNormals)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(m_Normals[v * 3 + 0]), 0, interleavedData, vertexStart + channels[1].offset + 0, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(m_Normals[v * 3 + 1]), 0, interleavedData, vertexStart + channels[1].offset + 4, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(m_Normals[v * 3 + 2]), 0, interleavedData, vertexStart + channels[1].offset + 8, 4);
                }

                // Tangents
                if (hasTangents)
                {
                    int chn = version.major >= 2018 ? 2 : (version.major >= 5 ? 7 : 5);
                    Buffer.BlockCopy(BitConverter.GetBytes(m_Tangents[v * 4 + 0]), 0, interleavedData, vertexStart + channels[chn].offset + 0, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(m_Tangents[v * 4 + 1]), 0, interleavedData, vertexStart + channels[chn].offset + 4, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(m_Tangents[v * 4 + 2]), 0, interleavedData, vertexStart + channels[chn].offset + 8, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(m_Tangents[v * 4 + 3]), 0, interleavedData, vertexStart + channels[chn].offset + 12, 4);
                }

                // Colors
                if (hasColors)
                {
                    int chn = version.major >= 2018 ? 3 : 2;
                    Buffer.BlockCopy(BitConverter.GetBytes(m_Colors[v * 4 + 0]), 0, interleavedData, vertexStart + channels[chn].offset + 0, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(m_Colors[v * 4 + 1]), 0, interleavedData, vertexStart + channels[chn].offset + 4, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(m_Colors[v * 4 + 2]), 0, interleavedData, vertexStart + channels[chn].offset + 8, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(m_Colors[v * 4 + 3]), 0, interleavedData, vertexStart + channels[chn].offset + 12, 4);
                }

                // UV0
                if (hasUV0)
                {
                    int chn = version.major >= 2018 ? 4 : 3;
                    Buffer.BlockCopy(BitConverter.GetBytes(m_UV0[v * 2 + 0]), 0, interleavedData, vertexStart + channels[chn].offset + 0, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(m_UV0[v * 2 + 1]), 0, interleavedData, vertexStart + channels[chn].offset + 4, 4);
                }

                // UV1
                if (hasUV1)
                {
                    int chn = version.major >= 2018 ? 5 : 4;
                    Buffer.BlockCopy(BitConverter.GetBytes(m_UV1[v * 2 + 0]), 0, interleavedData, vertexStart + channels[chn].offset + 0, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(m_UV1[v * 2 + 1]), 0, interleavedData, vertexStart + channels[chn].offset + 4, 4);
                }
            }

            m_VertexData ??= new VertexData();
            m_VertexData.m_VertexCount = (uint)m_VertexCount;
            m_VertexData.m_Channels = channels;
            m_VertexData.m_DataSize = interleavedData;
            m_VertexData.GetStreams(version);
        }

        public void GenerateNormals()
        {
            if (m_Vertices.Length == 0 || m_Indices.Count == 0) return;

            Vector3[] accumulatedNormals = new Vector3[m_VertexCount];

            for (int i = 0; i < m_Indices.Count; i += 3)
            {
                if (i + 2 >= m_Indices.Count) break;

                uint i0 = m_Indices[i];
                uint i1 = m_Indices[i + 1];
                uint i2 = m_Indices[i + 2];

                if (i0 >= m_VertexCount || i1 >= m_VertexCount || i2 >= m_VertexCount) continue;

                Vector3 p0 = new(m_Vertices[i0 * 3], m_Vertices[i0 * 3 + 1], m_Vertices[i0 * 3 + 2]);
                Vector3 p1 = new(m_Vertices[i1 * 3], m_Vertices[i1 * 3 + 1], m_Vertices[i1 * 3 + 2]);
                Vector3 p2 = new(m_Vertices[i2 * 3], m_Vertices[i2 * 3 + 1], m_Vertices[i2 * 3 + 2]);

                Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
                accumulatedNormals[i0] += normal;
                accumulatedNormals[i1] += normal;
                accumulatedNormals[i2] += normal;
            }

            m_Normals = new float[m_VertexCount * 3];
            for (int i = 0; i < m_VertexCount; i++)
            {
                Vector3 n = accumulatedNormals[i];
                if (n.LengthSquared() > 0.000001f)
                    n = Vector3.Normalize(n);
                else
                    n = Vector3.UnitY;

                m_Normals[i * 3 + 0] = n.X;
                m_Normals[i * 3 + 1] = n.Y;
                m_Normals[i * 3 + 2] = n.Z;
            }
        }

        public void GenerateTangents()
        {
            if (m_Vertices.Length == 0 || m_Indices.Count == 0 || m_UV0.Length != m_VertexCount * 2) return;

            if (m_Normals.Length != m_VertexCount * 3)
                GenerateNormals();

            Vector3[] tan1 = new Vector3[m_VertexCount];
            Vector3[] tan2 = new Vector3[m_VertexCount];

            for (int i = 0; i < m_Indices.Count; i += 3)
            {
                if (i + 2 >= m_Indices.Count) break;

                uint i0 = m_Indices[i];
                uint i1 = m_Indices[i + 1];
                uint i2 = m_Indices[i + 2];

                if (i0 >= m_VertexCount || i1 >= m_VertexCount || i2 >= m_VertexCount) continue;

                Vector3 v1 = new(m_Vertices[i0 * 3], m_Vertices[i0 * 3 + 1], m_Vertices[i0 * 3 + 2]);
                Vector3 v2 = new(m_Vertices[i1 * 3], m_Vertices[i1 * 3 + 1], m_Vertices[i1 * 3 + 2]);
                Vector3 v3 = new(m_Vertices[i2 * 3], m_Vertices[i2 * 3 + 1], m_Vertices[i2 * 3 + 2]);

                Vector2 w1 = new(m_UV0[i0 * 2], m_UV0[i0 * 2 + 1]);
                Vector2 w2 = new(m_UV0[i1 * 2], m_UV0[i1 * 2 + 1]);
                Vector2 w3 = new(m_UV0[i2 * 2], m_UV0[i2 * 2 + 1]);

                float x1 = v2.X - v1.X;
                float x2 = v3.X - v1.X;
                float y1 = v2.Y - v1.Y;
                float y2 = v3.Y - v1.Y;
                float z1 = v2.Z - v1.Z;
                float z2 = v3.Z - v1.Z;

                float s1 = w2.X - w1.X;
                float s2 = w3.X - w1.X;
                float t1 = w2.Y - w1.Y;
                float t2 = w3.Y - w1.Y;

                float r = (s1 * t2 - s2 * t1);
                float f = Math.Abs(r) > 0.000001f ? (1.0f / r) : 0.0f;

                Vector3 sdir = new((t2 * x1 - t1 * x2) * f, (t2 * y1 - t1 * y2) * f, (t2 * z1 - t1 * z2) * f);
                Vector3 tdir = new((s1 * x2 - s2 * x1) * f, (s1 * y2 - s2 * y1) * f, (s1 * z2 - s2 * z1) * f);

                tan1[i0] += sdir;
                tan1[i1] += sdir;
                tan1[i2] += sdir;

                tan2[i0] += tdir;
                tan2[i1] += tdir;
                tan2[i2] += tdir;
            }

            m_Tangents = new float[m_VertexCount * 4];
            for (int i = 0; i < m_VertexCount; i++)
            {
                Vector3 n = new(m_Normals[i * 3], m_Normals[i * 3 + 1], m_Normals[i * 3 + 2]);
                Vector3 t = tan1[i];

                // Gram-Schmidt orthogonalization
                Vector3 tangent = t - n * Vector3.Dot(n, t);
                if (tangent.LengthSquared() > 0.000001f)
                    tangent = Vector3.Normalize(tangent);
                else
                    tangent = Vector3.UnitX;

                // Calculate handedness (w component: +1.0 or -1.0)
                float w = (Vector3.Dot(Vector3.Cross(n, t), tan2[i]) < 0.0f) ? -1.0f : 1.0f;

                m_Tangents[i * 4 + 0] = tangent.X;
                m_Tangents[i * 4 + 1] = tangent.Y;
                m_Tangents[i * 4 + 2] = tangent.Z;
                m_Tangents[i * 4 + 3] = w;
            }
        }

        #endregion
    }
}
