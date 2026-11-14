using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public partial class MeshFile
    {
        #region UV helpers (UV0..UV7)

        public float[] GetUV(int channel) => channel switch
        {
            0 => m_UV0,
            1 => m_UV1,
            2 => m_UV2,
            3 => m_UV3,
            4 => m_UV4,
            5 => m_UV5,
            6 => m_UV6,
            7 => m_UV7,
            _ => Array.Empty<float>()
        };

        public void SetUV(int channel, float[] values)
        {
            values ??= Array.Empty<float>();
            switch (channel)
            {
                case 0: m_UV0 = values; break;
                case 1: m_UV1 = values; break;
                case 2: m_UV2 = values; break;
                case 3: m_UV3 = values; break;
                case 4: m_UV4 = values; break;
                case 5: m_UV5 = values; break;
                case 6: m_UV6 = values; break;
                case 7: m_UV7 = values; break;
            }
        }

        #endregion

        #region Rebuild Engine & Geometry Generators

        /// <summary>
        /// Rebuilds m_VertexData (all channels), index format, AABB and mesh metrics from the high-level arrays.
        /// </summary>
        public void RebuildBuffers(bool autoGenerateNormalsIfMissing = true, bool autoGenerateTangentsIfMissing = true)
        {
            if (m_Vertices.Length == 0)
                throw new InvalidOperationException("Cannot rebuild mesh with empty vertices.");

            m_VertexCount = m_Vertices.Length / 3;

            if (autoGenerateNormalsIfMissing && (m_Normals.Length != m_VertexCount * 3))
                GenerateNormals();

            if (autoGenerateTangentsIfMissing && (m_Tangents.Length != m_VertexCount * 4) && (m_UV0.Length == m_VertexCount * 2))
                GenerateTangents();

            m_Use16BitIndices = m_VertexCount <= 65535;

            RebuildIndexBufferAndSubMeshes();

            if (version.major > 3 || (version.major == 3 && version.minor >= 5))
                GenerateInterleavedVertexData();

            RecalculateBounds();

            if (m_LocalAABB != null && m_SubMeshes.Length > 0)
            {
                foreach (var sm in m_SubMeshes)
                    if (sm.localAABB == null) sm.localAABB = m_LocalAABB;
            }

            RecalculateMeshMetrics();

            m_StreamData = new StreamingInfo { offset = 0, size = 0, path = string.Empty };
            m_CompressedMesh = null;
            m_MeshCompression = 0;
            // m_IsReadable is left exactly as the caller set it.
        }

        private void RebuildIndexBufferAndSubMeshes()
        {
            if (m_Indices.Count > 0 && (m_SubMeshes == null || m_SubMeshes.Length == 0))
            {
                m_IndexBuffer = m_Indices.ToArray();
                m_SubMeshes = new[]
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
                m_Indices = m_IndexBuffer.ToList();
            }
        }

        /// <summary>semantic: 0 pos, 1 normal, 2 tangent, 3 color, 4..11 UV0..UV7  ->  Unity channel index (-1 = unsupported).</summary>
        private void BuildChannelMap(out int[] map, out int slots)
        {
            map = Enumerable.Repeat(-1, 12).ToArray();

            if (version.major >= 2018)
            {
                slots = 14;
                for (int i = 0; i < 12; i++) map[i] = i;
            }
            else if (version.major >= 5)
            {
                // 0 pos, 1 normal, 2 color, 3 uv0, 4 uv1, 5 uv2, 6 uv3, 7 tangent  (UV4..7 not available)
                slots = 8;
                map[0] = 0; map[1] = 1; map[3] = 2;
                map[4] = 3; map[5] = 4; map[6] = 5; map[7] = 6;
                map[2] = 7;
            }
            else
            {
                // 0 pos, 1 normal, 2 color, 3 uv0, 4 uv1, 5 tangent  (UV2..7 not available)
                slots = 6;
                map[0] = 0; map[1] = 1; map[3] = 2;
                map[4] = 3; map[5] = 4;
                map[2] = 5;
            }
        }

        private void GenerateInterleavedVertexData()
        {
            BuildChannelMap(out int[] map, out int slots);

            var channels = new ChannelInfo[slots];
            for (int i = 0; i < slots; i++)
                channels[i] = new ChannelInfo { stream = 0, offset = 0, format = 0, dimension = 0 };

            bool oldLayout = version.major < 2018;
            var attrs = new List<(int ch, float[] src, int dim, bool unorm8)>();

            void Add(int semantic, float[] src, int dim, bool unorm8 = false)
            {
                int ch = map[semantic];
                if (ch < 0 || src == null || src.Length != m_VertexCount * dim) return;
                attrs.Add((ch, src, dim, unorm8));
            }

            Add(0, m_Vertices, 3);
            Add(1, m_Normals, 3);
            if (!oldLayout) Add(2, m_Tangents, 4);
            Add(3, m_Colors, 4, oldLayout);                 // old Unity: color = UNorm8
            for (int u = 0; u < 8; u++) Add(4 + u, GetUV(u), 2);
            if (oldLayout) Add(2, m_Tangents, 4);

            int offset = 0;
            foreach (var a in attrs)
            {
                channels[a.ch].stream = 0;
                channels[a.ch].offset = (byte)offset;
                channels[a.ch].dimension = (byte)a.dim;
                channels[a.ch].format = (byte)(a.unorm8 ? 2 : 0);
                offset += a.unorm8 ? a.dim : a.dim * 4;
            }

            int stride = offset;
            byte[] data = new byte[m_VertexCount * stride];

            for (int v = 0; v < m_VertexCount; v++)
            {
                int vertexStart = v * stride;
                foreach (var a in attrs)
                {
                    int dst = vertexStart + channels[a.ch].offset;
                    if (a.unorm8)
                    {
                        for (int d = 0; d < a.dim; d++)
                            data[dst + d] = (byte)Math.Clamp((int)MathF.Round(a.src[v * a.dim + d] * 255f), 0, 255);
                    }
                    else
                    {
                        Buffer.BlockCopy(a.src, v * a.dim * 4, data, dst, a.dim * 4);
                    }
                }
            }

            m_VertexData ??= new VertexData();
            m_VertexData.m_VertexCount = (uint)m_VertexCount;
            m_VertexData.m_Channels = channels;
            m_VertexData.m_DataSize = data;
            m_VertexData.GetStreams(version);
        }

        #endregion

        #region Mesh Metrics (UV density)

        /// <summary>m_MeshMetrics[0] = UV0 density, [1] = UV1 density (approximation: sum(world area) / sum(uv area), 1.0 if degenerate).</summary>
        public void RecalculateMeshMetrics()
        {
            m_MeshMetrics[0] = ComputeUVDensity(m_UV0);
            m_MeshMetrics[1] = ComputeUVDensity(m_UV1);
        }

        private float ComputeUVDensity(float[] uv)
        {
            if (uv == null || uv.Length != m_VertexCount * 2 || m_IndexBuffer.Length < 3) return 1.0f;

            double worldArea = 0, uvArea = 0;
            for (int i = 0; i + 2 < m_IndexBuffer.Length; i += 3)
            {
                int a = (int)m_IndexBuffer[i], b = (int)m_IndexBuffer[i + 1], c = (int)m_IndexBuffer[i + 2];
                if (a >= m_VertexCount || b >= m_VertexCount || c >= m_VertexCount) continue;

                var p0 = new Vector3(m_Vertices[a * 3], m_Vertices[a * 3 + 1], m_Vertices[a * 3 + 2]);
                var p1 = new Vector3(m_Vertices[b * 3], m_Vertices[b * 3 + 1], m_Vertices[b * 3 + 2]);
                var p2 = new Vector3(m_Vertices[c * 3], m_Vertices[c * 3 + 1], m_Vertices[c * 3 + 2]);
                worldArea += 0.5 * Vector3.Cross(p1 - p0, p2 - p0).Length();

                float ux1 = uv[b * 2] - uv[a * 2], uy1 = uv[b * 2 + 1] - uv[a * 2 + 1];
                float ux2 = uv[c * 2] - uv[a * 2], uy2 = uv[c * 2 + 1] - uv[a * 2 + 1];
                uvArea += 0.5 * Math.Abs(ux1 * uy2 - ux2 * uy1);
            }

            if (uvArea < 1e-12 || worldArea < 1e-12) return 1.0f;
            return (float)(worldArea / uvArea);
        }

        #endregion

        #region Normals / Tangents generators

        public void GenerateNormals()
        {
            if (m_Vertices.Length == 0 || m_Indices.Count == 0) return;

            Vector3[] acc = new Vector3[m_VertexCount];

            for (int i = 0; i + 2 < m_Indices.Count; i += 3)
            {
                uint i0 = m_Indices[i], i1 = m_Indices[i + 1], i2 = m_Indices[i + 2];
                if (i0 >= m_VertexCount || i1 >= m_VertexCount || i2 >= m_VertexCount) continue;

                Vector3 p0 = new(m_Vertices[i0 * 3], m_Vertices[i0 * 3 + 1], m_Vertices[i0 * 3 + 2]);
                Vector3 p1 = new(m_Vertices[i1 * 3], m_Vertices[i1 * 3 + 1], m_Vertices[i1 * 3 + 2]);
                Vector3 p2 = new(m_Vertices[i2 * 3], m_Vertices[i2 * 3 + 1], m_Vertices[i2 * 3 + 2]);

                Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
                acc[i0] += normal; acc[i1] += normal; acc[i2] += normal;
            }

            m_Normals = new float[m_VertexCount * 3];
            for (int i = 0; i < m_VertexCount; i++)
            {
                Vector3 n = acc[i];
                n = n.LengthSquared() > 0.000001f ? Vector3.Normalize(n) : Vector3.UnitY;
                m_Normals[i * 3] = n.X; m_Normals[i * 3 + 1] = n.Y; m_Normals[i * 3 + 2] = n.Z;
            }
        }

        public void GenerateTangents()
        {
            if (m_Vertices.Length == 0 || m_Indices.Count == 0 || m_UV0.Length != m_VertexCount * 2) return;
            if (m_Normals.Length != m_VertexCount * 3) GenerateNormals();

            Vector3[] tan1 = new Vector3[m_VertexCount];
            Vector3[] tan2 = new Vector3[m_VertexCount];

            for (int i = 0; i + 2 < m_Indices.Count; i += 3)
            {
                uint i0 = m_Indices[i], i1 = m_Indices[i + 1], i2 = m_Indices[i + 2];
                if (i0 >= m_VertexCount || i1 >= m_VertexCount || i2 >= m_VertexCount) continue;

                Vector3 v1 = new(m_Vertices[i0 * 3], m_Vertices[i0 * 3 + 1], m_Vertices[i0 * 3 + 2]);
                Vector3 v2 = new(m_Vertices[i1 * 3], m_Vertices[i1 * 3 + 1], m_Vertices[i1 * 3 + 2]);
                Vector3 v3 = new(m_Vertices[i2 * 3], m_Vertices[i2 * 3 + 1], m_Vertices[i2 * 3 + 2]);

                Vector2 w1 = new(m_UV0[i0 * 2], m_UV0[i0 * 2 + 1]);
                Vector2 w2 = new(m_UV0[i1 * 2], m_UV0[i1 * 2 + 1]);
                Vector2 w3 = new(m_UV0[i2 * 2], m_UV0[i2 * 2 + 1]);

                float x1 = v2.X - v1.X, x2 = v3.X - v1.X;
                float y1 = v2.Y - v1.Y, y2 = v3.Y - v1.Y;
                float z1 = v2.Z - v1.Z, z2 = v3.Z - v1.Z;

                float s1 = w2.X - w1.X, s2 = w3.X - w1.X;
                float t1 = w2.Y - w1.Y, t2 = w3.Y - w1.Y;

                float r = s1 * t2 - s2 * t1;
                float f = Math.Abs(r) > 0.000001f ? 1.0f / r : 0.0f;

                Vector3 sdir = new((t2 * x1 - t1 * x2) * f, (t2 * y1 - t1 * y2) * f, (t2 * z1 - t1 * z2) * f);
                Vector3 tdir = new((s1 * x2 - s2 * x1) * f, (s1 * y2 - s2 * y1) * f, (s1 * z2 - s2 * z1) * f);

                tan1[i0] += sdir; tan1[i1] += sdir; tan1[i2] += sdir;
                tan2[i0] += tdir; tan2[i1] += tdir; tan2[i2] += tdir;
            }

            m_Tangents = new float[m_VertexCount * 4];
            for (int i = 0; i < m_VertexCount; i++)
            {
                Vector3 n = new(m_Normals[i * 3], m_Normals[i * 3 + 1], m_Normals[i * 3 + 2]);
                Vector3 t = tan1[i];

                Vector3 tangent = t - n * Vector3.Dot(n, t);
                tangent = tangent.LengthSquared() > 0.000001f ? Vector3.Normalize(tangent) : Vector3.UnitX;

                float w = (Vector3.Dot(Vector3.Cross(n, t), tan2[i]) < 0.0f) ? -1.0f : 1.0f;

                m_Tangents[i * 4] = tangent.X; m_Tangents[i * 4 + 1] = tangent.Y;
                m_Tangents[i * 4 + 2] = tangent.Z; m_Tangents[i * 4 + 3] = w;
            }
        }

        #endregion
    }
}