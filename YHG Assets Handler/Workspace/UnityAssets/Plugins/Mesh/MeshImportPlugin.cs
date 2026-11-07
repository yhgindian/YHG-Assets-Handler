using Assimp;
using Assimp.Configs;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Mesh
{
    internal class MeshImportPlugin : IInternalAssetPlugin
    {
        public string DisplayName => "Import / Replace OBJ Mesh";
        public int TargetClassId => (int)AssetClassID.Mesh; // 43
        public PluginTargetMode TargetMode => PluginTargetMode.Single;

        public async Task ExecuteAsync(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets)
        {
            if (selectedAssets == null || selectedAssets.Count == 0) return;
            var assetModel = selectedAssets[0];
            var cont = assetModel.assetContainer;
            if (cont?.FileInstance == null) return;

            // 1. File Picker (OBJ only)
            var ofd = new OpenFileDialog
            {
                Filter = "Wavefront 3D Object (*.obj)|*.obj",
                Title = "Select Wavefront OBJ to Import"
            };

            if (ofd.ShowDialog() != true) return;
            string objPath = ofd.FileName;
            string meshName = Path.GetFileNameWithoutExtension(objPath);

            // 2. Background Process with Live Percentage Progress
            await ProcessManager.RunAsync("Importing OBJ Mesh", async handler =>
            {
                await Task.Run(() =>
                {
                    handler.Report(5, "Initializing Assimp Context...");

                    using var context = new AssimpContext();
                    context.SetConfig(new NormalSmoothingAngleConfig(66.0f));

                    handler.Report(15, "Parsing Wavefront OBJ via Assimp...");
                    var postProcessSteps = PostProcessSteps.Triangulate |
                                           PostProcessSteps.GenerateNormals |
                                           PostProcessSteps.CalculateTangentSpace |
                                           PostProcessSteps.JoinIdenticalVertices |
                                           PostProcessSteps.ImproveCacheLocality;

                    Scene scene = context.ImportFile(objPath, postProcessSteps);

                    if (scene == null || !scene.HasMeshes)
                        throw new InvalidOperationException($"Assimp failed to read mesh from: {objPath}");

                    handler.Report(40, "Processing Scene Meshes & Aligning Coordinates...");

                    var unityVersion = cont.FileInstance.file.Metadata.UnityVersion;
                    var meshFile = new MeshFile
                    {
                        version = new UnityVersion(unityVersion),
                        m_Name = meshName
                    };

                    // Aggregate high-level components from Assimp Scene
                    PopulateMeshFileFromAssimpScene(scene, meshFile, handler);

                    handler.Report(75, "Rebuilding Low-Level Interleaved Buffers...");
                    // RebuildBuffers automatically packs 16/32-bit indices, channels, streams, AABB & metrics
                    meshFile.RebuildBuffers(autoGenerateNormalsIfMissing: false, autoGenerateTangentsIfMissing: false);

                    handler.Report(85, "Resolving Asset TypeTree...");
                    var baseField = cont.BaseValueField;
                    if (baseField == null || baseField.IsDummy)
                    {
                        var fullCont = workspace.GetAssetContainer(cont.FileInstance, 0, cont.PathId);
                        baseField = fullCont?.BaseValueField;
                    }

                    if (baseField == null || baseField.IsDummy)
                        throw new InvalidOperationException("Failed to access valid TypeTree base field for the selected mesh.");

                    handler.Report(90, "Writing MeshFile into TypeTree...");
                    meshFile.WriteTo(baseField);

                    handler.Report(95, "Creating Replacer & Committing to Workspace...");
                    byte[] data = baseField.WriteToByteArray();
                    workspace.AddOrUpdateAsset(cont, data);
                    handler.Report(100, "OBJ imported and replaced successfully.");
                }, handler.Token);
            });

            NotificationManager.Success(assetModel.Name, "OBJ mesh successfully imported and registered in replacer queue.");
        }

        private static void PopulateMeshFileFromAssimpScene(Scene scene, MeshFile targetMesh, IProcessHandle handler)
        {
            List<float> allVertices = new();
            List<float> allNormals = new();
            List<float> allTangents = new();
            List<float> allUV0 = new();
            List<float> allColors = new();
            List<uint> allIndices = new();
            List<SubMesh> subMeshes = new();

            uint totalVertexCount = 0;
            bool sceneHasNormals = false;
            bool sceneHasTangents = false;
            bool sceneHasUVs = false;
            bool sceneHasColors = false;

            // Pre-scan channels
            foreach (var m in scene.Meshes)
            {
                totalVertexCount += (uint)m.VertexCount;
                if (m.HasNormals) sceneHasNormals = true;
                if (m.HasTangentBasis) sceneHasTangents = true;
                if (m.HasTextureCoords(0)) sceneHasUVs = true;
                if (m.HasVertexColors(0)) sceneHasColors = true;
            }

            bool use32Bit = totalVertexCount > 65535;
            targetMesh.m_Use16BitIndices = !use32Bit;

            uint globalVertexAccum = 0;
            uint globalIndexAccum = 0;

            for (int meshIdx = 0; meshIdx < scene.Meshes.Count; meshIdx++)
            {
                var mesh = scene.Meshes[meshIdx];
                int meshVertCount = mesh.VertexCount;

                // Live dynamic percentage reporting between 40% and 75%
                int progress = 40 + (int)(((float)meshIdx / scene.Meshes.Count) * 35);
                handler.Report(progress, $"Processing SubMesh {meshIdx + 1}/{scene.Meshes.Count} ({meshVertCount} verts)...");

                // 1. Vertices & Attributes
                for (int v = 0; v < meshVertCount; v++)
                {
                    // Unity Coordinate Swap: Invert X for Left-Handed space
                    var pos = mesh.Vertices[v];
                    allVertices.Add(-pos.X);
                    allVertices.Add(pos.Y);
                    allVertices.Add(pos.Z);

                    if (sceneHasNormals)
                    {
                        var norm = mesh.HasNormals ? mesh.Normals[v] : new Vector3D(0, 1, 0);
                        allNormals.Add(-norm.X);
                        allNormals.Add(norm.Y);
                        allNormals.Add(norm.Z);
                    }

                    if (sceneHasTangents)
                    {
                        var tan = mesh.HasTangentBasis ? mesh.Tangents[v] : new Vector3D(1, 0, 0);
                        allTangents.Add(-tan.X);
                        allTangents.Add(tan.Y);
                        allTangents.Add(tan.Z);
                        allTangents.Add(1.0f); // Default W component
                    }

                    if (sceneHasUVs)
                    {
                        var uv = mesh.HasTextureCoords(0) ? mesh.TextureCoordinateChannels[0][v] : new Vector3D(0, 0, 0);
                        allUV0.Add(uv.X);
                        allUV0.Add(uv.Y);
                    }

                    if (sceneHasColors)
                    {
                        var col = mesh.HasVertexColors(0) ? mesh.VertexColorChannels[0][v] : new Color4D(1, 1, 1, 1);
                        allColors.Add(col.R);
                        allColors.Add(col.G);
                        allColors.Add(col.B);
                        allColors.Add(col.A);
                    }
                }

                // 2. Indices & Faces (Triangle Winding Order: [0, 2, 1] for Unity)
                List<uint> currentSubMeshIndices = new();
                foreach (var face in mesh.Faces)
                {
                    if (face.IndexCount != 3) continue;

                    uint i0 = (uint)(face.Indices[0] + globalVertexAccum);
                    uint i1 = (uint)(face.Indices[2] + globalVertexAccum);
                    uint i2 = (uint)(face.Indices[1] + globalVertexAccum);

                    currentSubMeshIndices.Add(i0);
                    currentSubMeshIndices.Add(i1);
                    currentSubMeshIndices.Add(i2);

                    allIndices.Add(i0);
                    allIndices.Add(i1);
                    allIndices.Add(i2);
                }

                // 3. SubMesh Structure & Local AABB
                uint subMeshIndexCount = (uint)currentSubMeshIndices.Count;
                var subMeshAABB = CalculateSubMeshAABB(allVertices, currentSubMeshIndices);

                subMeshes.Add(new SubMesh
                {
                    firstByte = globalIndexAccum * (use32Bit ? 4u : 2u),
                    indexCount = subMeshIndexCount,
                    topology = MeshHelper.GfxPrimitiveType.Triangles,
                    triangleCount = subMeshIndexCount / 3,
                    baseVertex = 0,
                    firstVertex = globalVertexAccum,
                    vertexCount = (uint)meshVertCount,
                    localAABB = subMeshAABB
                });

                globalVertexAccum += (uint)meshVertCount;
                globalIndexAccum += subMeshIndexCount;
            }

            // Assign pure high-level structures to MeshFile
            targetMesh.m_VertexCount = (int)totalVertexCount;
            targetMesh.m_Vertices = allVertices.ToArray();
            targetMesh.m_Normals = allNormals.ToArray();
            targetMesh.m_Tangents = allTangents.ToArray();
            targetMesh.m_UV0 = allUV0.ToArray();
            targetMesh.m_Colors = allColors.ToArray();
            targetMesh.m_Indices = allIndices;
            targetMesh.m_IndexBuffer = allIndices.ToArray();
            targetMesh.m_SubMeshes = subMeshes.ToArray();
        }

        private static AABB CalculateSubMeshAABB(List<float> vertices, List<uint> indices)
        {
            if (indices.Count == 0)
                return new AABB { m_Center = Vector3.Zero, m_Extent = Vector3.Zero };

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            foreach (var idx in indices)
            {
                int baseIdx = (int)idx * 3;
                if (baseIdx + 2 >= vertices.Count) continue;

                float x = vertices[baseIdx];
                float y = vertices[baseIdx + 1];
                float z = vertices[baseIdx + 2];

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (z < minZ) minZ = z;

                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
                if (z > maxZ) maxZ = z;
            }

            return new AABB
            {
                m_Center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f),
                m_Extent = new Vector3((maxX - minX) * 0.5f, (maxY - minY) * 0.5f, (maxZ - minZ) * 0.5f)
            };
        }
    }
}