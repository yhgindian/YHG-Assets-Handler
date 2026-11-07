using Assimp;
using System;
using System.IO;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public partial class MeshFile
    {
        /// <summary>
        /// Universal export method using Assimp engine.
        /// Supports: .glb, .gltf, .dae, .obj, .ply, .stl
        /// </summary>
        public void ExportWithAssimp(string outputPath, string formatExtension, bool splitSubmeshesAsObjects = false)
        {
            if (m_VertexCount <= 0 || m_Vertices == null || m_Vertices.Length == 0)
                throw new InvalidOperationException("Cannot export empty or uninitialized mesh geometry.");

            string formatId = MapExtensionToAssimpFormatId(formatExtension);

            var scene = new Scene();
            var rootNode = new Node(string.IsNullOrEmpty(m_Name) ? "RootNode" : m_Name);
            scene.RootNode = rootNode;

            bool hasNormals = m_Normals != null && m_Normals.Length == m_VertexCount * 3;
            bool hasTangents = m_Tangents != null && m_Tangents.Length == m_VertexCount * 4;
            bool hasUV0 = m_UV0 != null && m_UV0.Length == m_VertexCount * 2;
            bool hasColors = m_Colors != null && m_Colors.Length == m_VertexCount * 4;

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
                    PopulateAssimpVertices(mesh, hasNormals, hasTangents, hasUV0, hasColors);

                    int firstIndex = (int)(subMesh.firstByte / (m_Use16BitIndices ? 2 : 4));

                    for (int i = 0; i < subMesh.indexCount; i += 3)
                    {
                        if (firstIndex + i + 2 >= m_IndexBuffer.Length) break;

                        int i0 = (int)m_IndexBuffer[firstIndex + i];
                        int i1 = (int)m_IndexBuffer[firstIndex + i + 2];
                        int i2 = (int)m_IndexBuffer[firstIndex + i + 1];

                        mesh.Faces.Add(new Face(new[] { i0, i1, i2 }));
                    }

                    var mat = new Material { Name = $"Material_{subMeshName}" };
                    scene.Materials.Add(mat);
                    mesh.MaterialIndex = scene.Materials.Count - 1;

                    int currentMeshIndex = scene.Meshes.Count;
                    scene.Meshes.Add(mesh);

                    // 👉 YAHAN SE CONTROL HOTA HAI KI ALAG OBJECT BANEGA YA EK HI OBJECT
                    if (splitSubmeshesAsObjects)
                    {
                        // Har submesh ka apna independent Child Node -> Blender me ALAG OBJECT banega
                        var subMeshNode = new Node(subMeshName);
                        subMeshNode.MeshIndices.Add(currentMeshIndex);
                        rootNode.Children.Add(subMeshNode);
                    }
                    else
                    {
                        // Root node se direct bind -> Blender me SINGLE OBJECT with multiple materials banega
                        rootNode.MeshIndices.Add(currentMeshIndex);
                    }
                }
            }

            using var context = new AssimpContext();
            bool exported = context.ExportFile(scene, outputPath, formatId);

            if (!exported)
            {
                throw new InvalidOperationException($"Assimp exporter failed for format: '{formatId}'");
            }
        }

        private static string MapExtensionToAssimpFormatId(string ext)
        {
            return ext.ToLowerInvariant().TrimStart('.') switch
            {
                "glb" => "glb2",     // Binary glTF 2.0
                "gltf" => "gltf2",    // JSON glTF 2.0
                "dae" => "collada",  // Collada DAE
                "obj" => "obj",      // Wavefront OBJ
                "ply" => "ply",      // Stanford Polygon Library
                "stl" => "stl",      // Stereolithography (3D Print)
                _ => "glb2"
            };
        }

        private void PopulateAssimpVertices(
            Assimp.Mesh mesh,
            bool hasNormals,
            bool hasTangents,
            bool hasUV0,
            bool hasColors)
        {
            for (int v = 0; v < m_VertexCount; v++)
            {
                // Unity Left-Handed to glTF Right-Handed: Invert X
                float vx = -m_Vertices[v * 3 + 0];
                float vy = m_Vertices[v * 3 + 1];
                float vz = m_Vertices[v * 3 + 2];
                mesh.Vertices.Add(new Vector3D(vx, vy, vz));

                if (hasNormals)
                {
                    mesh.Normals.Add(new Vector3D(
                        -m_Normals[v * 3 + 0],
                        m_Normals[v * 3 + 1],
                        m_Normals[v * 3 + 2]
                    ));
                }

                if (hasTangents)
                {
                    mesh.Tangents.Add(new Vector3D(
                        -m_Tangents[v * 4 + 0],
                        m_Tangents[v * 4 + 1],
                        m_Tangents[v * 4 + 2]
                    ));
                }

                if (hasUV0)
                {
                    // glTF standard V-coordinate alignment
                    mesh.TextureCoordinateChannels[0].Add(new Vector3D(
                        m_UV0[v * 2 + 0],
                        1.0f - m_UV0[v * 2 + 1],
                        0.0f
                    ));
                }

                if (hasColors)
                {
                    mesh.VertexColorChannels[0].Add(new Color4D(
                        m_Colors[v * 4 + 0],
                        m_Colors[v * 4 + 1],
                        m_Colors[v * 4 + 2],
                        m_Colors[v * 4 + 3]
                    ));
                }
            }
        }
    }
}