using Assimp;
using System;
using System.IO;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public class MeshExportOptions
    {
        public bool FlipX = true;             // Unity (left-handed) -> right-handed
        public bool FlipV = true;             // Unity V-up -> top-left origin
        public bool SplitSubmeshesAsObjects;  // each submesh = separate object
    }

    public partial class MeshFile
    {
        /// <summary>
        /// Universal export using Assimp. Supports: .glb, .gltf, .dae, .obj, .ply, .stl
        /// Exports ALL channels: position, normal, tangent, color, UV0..UV7.
        /// NOTE: .obj can only carry UV0 (format limitation).
        /// </summary>
        public void ExportWithAssimp(string outputPath, string formatExtension, MeshExportOptions? options = null)
        {
            options ??= new MeshExportOptions();

            if (m_VertexCount <= 0 || m_Vertices == null || m_Vertices.Length == 0)
                throw new InvalidOperationException("Cannot export empty or uninitialized mesh geometry.");

            string formatId = MapExtensionToAssimpFormatId(formatExtension);

            var scene = new Scene();
            var rootNode = new Node(string.IsNullOrEmpty(m_Name) ? "RootNode" : m_Name);
            scene.RootNode = rootNode;

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (m_SubMeshes != null && m_SubMeshes.Length > 0 && m_IndexBuffer != null && m_IndexBuffer.Length > 0)
            {
                for (int sm = 0; sm < m_SubMeshes.Length; sm++)
                {
                    var subMesh = m_SubMeshes[sm];
                    string subMeshName = $"{m_Name}_SubMesh_{sm}";

                    var mesh = new Assimp.Mesh(subMeshName, PrimitiveType.Triangle);
                    PopulateAssimpVertices(mesh, options);

                    int firstIndex = (int)(subMesh.firstByte / (m_Use16BitIndices ? 2 : 4));

                    for (int i = 0; i < subMesh.indexCount; i += 3)
                    {
                        if (firstIndex + i + 2 >= m_IndexBuffer.Length) break;

                        int i0 = (int)m_IndexBuffer[firstIndex + i];
                        int i1 = (int)m_IndexBuffer[firstIndex + i + (options.FlipX ? 2 : 1)];
                        int i2 = (int)m_IndexBuffer[firstIndex + i + (options.FlipX ? 1 : 2)];

                        mesh.Faces.Add(new Face(new[] { i0, i1, i2 }));
                    }

                    var mat = new Material { Name = $"Material_{subMeshName}" };
                    scene.Materials.Add(mat);
                    mesh.MaterialIndex = scene.Materials.Count - 1;

                    int currentMeshIndex = scene.Meshes.Count;
                    scene.Meshes.Add(mesh);

                    if (options.SplitSubmeshesAsObjects)
                    {
                        var node = new Node(subMeshName);
                        node.MeshIndices.Add(currentMeshIndex);
                        rootNode.Children.Add(node);
                    }
                    else
                    {
                        rootNode.MeshIndices.Add(currentMeshIndex);
                    }
                }
            }

            using var context = new AssimpContext();
            if (!context.ExportFile(scene, outputPath, formatId))
                throw new InvalidOperationException($"Assimp exporter failed for format: '{formatId}'");
        }

        private static string MapExtensionToAssimpFormatId(string ext)
        {
            return ext.ToLowerInvariant().TrimStart('.') switch
            {
                "glb" => "glb2",
                "gltf" => "gltf2",
                "dae" => "collada",
                "obj" => "obj",
                "ply" => "ply",
                "stl" => "stl",
                _ => "glb2"
            };
        }

        private void PopulateAssimpVertices(Assimp.Mesh mesh, MeshExportOptions o)
        {
            float sx = o.FlipX ? -1f : 1f;

            bool hasNormals = m_Normals != null && m_Normals.Length == m_VertexCount * 3;
            bool hasTangents = m_Tangents != null && m_Tangents.Length == m_VertexCount * 4;
            bool hasColors = m_Colors != null && m_Colors.Length == m_VertexCount * 4;

            var uvArrays = new float[8][];
            var hasUV = new bool[8];
            for (int c = 0; c < 8; c++)
            {
                uvArrays[c] = GetUV(c);
                hasUV[c] = uvArrays[c] != null && uvArrays[c].Length == m_VertexCount * 2;
                if (hasUV[c]) mesh.UVComponentCount[c] = 2;
            }

            for (int v = 0; v < m_VertexCount; v++)
            {
                mesh.Vertices.Add(new Vector3D(sx * m_Vertices[v * 3], m_Vertices[v * 3 + 1], m_Vertices[v * 3 + 2]));

                if (m_Normals != null && hasNormals)
                    mesh.Normals.Add(new Vector3D(sx * m_Normals[v * 3], m_Normals[v * 3 + 1], m_Normals[v * 3 + 2]));

                if (m_Tangents != null && hasTangents)
                    mesh.Tangents.Add(new Vector3D(sx * m_Tangents[v * 4], m_Tangents[v * 4 + 1], m_Tangents[v * 4 + 2]));

                for (int c = 0; c < 8; c++)
                {
                    if (!hasUV[c]) continue;
                    float u = uvArrays[c][v * 2];
                    float vv = uvArrays[c][v * 2 + 1];
                    mesh.TextureCoordinateChannels[c].Add(new Vector3D(u, o.FlipV ? 1.0f - vv : vv, 0f));
                }

                if (m_Colors != null && hasColors)
                    mesh.VertexColorChannels[0].Add(new Color4D(
                        m_Colors[v * 4], m_Colors[v * 4 + 1], m_Colors[v * 4 + 2], m_Colors[v * 4 + 3]));
            }
        }
    }
}