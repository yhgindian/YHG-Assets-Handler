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
using YHG_Assets_Handler.Views.Windows;
using YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Mesh
{
    internal class MeshImportPlugin : IInternalAssetPlugin
    {
        public string DisplayName => "Import / Replace Mesh (OBJ / GLB / GLTF / DAE ...)";
        public int TargetClassId => (int)AssetClassID.Mesh; // 43
        public PluginTargetMode TargetMode => PluginTargetMode.Single;

        private sealed class ImportOptions
        {
            public string Name = "Mesh";
            public bool FlipX = true;
            public bool FlipV = true;
            public float SmoothAngle = 66f;
            public bool GenerateTangents;
            public bool Readable = true;
        }

        public async Task ExecuteAsync(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets)
        {
            if (selectedAssets == null || selectedAssets.Count == 0) return;
            var assetModel = selectedAssets[0];
            var cont = assetModel.assetContainer;
            if (cont?.FileInstance == null) return;

            var ofd = new OpenFileDialog
            {
                Filter = "3D Mesh (*.glb;*.gltf;*.obj;*.dae;*.ply;*.stl)|*.glb;*.gltf;*.obj;*.dae;*.ply;*.stl|All Files (*.*)|*.*",
                Title = "Select Mesh to Import"
            };
            if (ofd.ShowDialog() != true) return;

            string meshPath = ofd.FileName;
            string ext = Path.GetExtension(meshPath).ToLowerInvariant();
            bool isGltf = ext == ".glb" || ext == ".gltf";

            var pName = new OptionParam("name", "Mesh name", OptionKind.Text,
                Path.GetFileNameWithoutExtension(meshPath), "Asset's new Name")
            {
                Validator = v => string.IsNullOrWhiteSpace(v as string) ? "Name can't be empty." : null
            };

            var pFlipX = new OptionParam("flipX", "Invert X axis (Right-handed → Unity)", OptionKind.Bool, true,
                "Enable for meshes imported from Blender, glTF, or OBJ. Triangle winding is corrected automatically.");

            var pFlipV = new OptionParam("flipV", "Flip V (UV vertical)", OptionKind.Bool, !isGltf,
                "Applies to all UV channels (UV0–UV7). The Assimp glTF importer already flips V, so this is disabled by default for GLB/GLTF.");

            var pSmooth = new OptionParam("smooth", "Normal smoothing angle (degrees)", OptionKind.Number, 66.0,
                "Only applies when the source file does not contain normals.")
            {
                Validator = v => v is double d && d >= 0 && d <= 180
                    ? null
                    : "Enter a value between 0 and 180."
            };

            var pTan = new OptionParam("tangents", "Generate tangents if missing", OptionKind.Bool, false,
                "Enable when using normal maps. Requires UV0.");

            var pRead = new OptionParam("readable", "Read/Write enabled", OptionKind.Bool, true,
                "Controls m_IsReadable, KeepVertices, and KeepIndices.");


            var result = OptionSelectorWindow.Ask(null, "Import Mesh Options",
                new List<OptionParam> { pName, pFlipX, pFlipV, pSmooth, pTan, pRead },
                $"File: {Path.GetFileName(meshPath)}");
            if (result == null) return;

            var opt = new ImportOptions
            {
                Name = (result["name"] as string) ?? "Mesh",
                FlipX = result["flipX"] is bool bx && bx,
                FlipV = result["flipV"] is bool bv && bv,
                SmoothAngle = (float)(result["smooth"] is double sd ? sd : 66.0),
                GenerateTangents = result["tangents"] is bool bt && bt,
                Readable = result["readable"] is bool br && br
            };

            await ProcessManager.RunAsync("Importing Mesh", async handler =>
            {
                await Task.Run(() =>
                {
                    handler.Report(5, "Resolving Asset TypeTree...");
                    var baseField = cont.BaseValueField;
                    if (baseField == null || baseField.IsDummy)
                    {
                        var fullCont = workspace.GetAssetContainer(cont.FileInstance, 0, cont.PathId);
                        baseField = fullCont?.BaseValueField;
                    }
                    if (baseField == null || baseField.IsDummy)
                        throw new InvalidOperationException("Failed to access valid TypeTree base field for the selected mesh.");

                    var unityVersion = new UnityVersion(cont.FileInstance.file.Metadata.UnityVersion);

                    handler.Report(10, "Initializing Assimp Context...");
                    using var context = new AssimpContext();
                    context.SetConfig(new NormalSmoothingAngleConfig(opt.SmoothAngle));

                    handler.Report(15, "Parsing mesh via Assimp...");
                    var steps = PostProcessSteps.Triangulate |
                                PostProcessSteps.GenerateNormals |
                                PostProcessSteps.JoinIdenticalVertices;
                    if (opt.GenerateTangents) steps |= PostProcessSteps.CalculateTangentSpace;

                    Scene scene = context.ImportFile(meshPath, steps);
                    if (scene == null || !scene.HasMeshes)
                        throw new InvalidOperationException($"Assimp failed to read mesh from: {meshPath}");

                    handler.Report(40, "Reading all channels...");

                    var meshFile = new MeshFile
                    {
                        version = unityVersion,
                        m_Name = opt.Name,
                        m_IsReadable = opt.Readable,
                        m_KeepVertices = opt.Readable,
                        m_KeepIndices = opt.Readable,
                        m_MeshUsageFlags = 0,
                        m_CookingOptions = 30   // Unity default
                    };

                    PopulateMeshFileFromAssimpScene(scene, meshFile, handler, opt);

                    handler.Report(75, "Rebuilding Low-Level Interleaved Buffers...");
                    meshFile.RebuildBuffers(autoGenerateNormalsIfMissing: false, autoGenerateTangentsIfMissing: false);

                    handler.Report(90, "Writing MeshFile into TypeTree...");
                    meshFile.WriteTo(baseField);

                    handler.Report(95, "Creating Replacer & Committing to Workspace...");
                    byte[] data = baseField.WriteToByteArray();
                    workspace.AddOrUpdateAsset(cont, data);
                    handler.Report(100, "Mesh imported and replaced successfully.");
                }, handler.Token);
            });

            NotificationManager.Success(assetModel.Name, "Mesh successfully imported and registered in replacer queue.");
        }

        private static void PopulateMeshFileFromAssimpScene(Scene scene, MeshFile targetMesh, IProcessHandle handler, ImportOptions opt)
        {
            const int UV_CHANNELS = 8;
            float sx = opt.FlipX ? -1f : 1f;   // X mirror
            float sv = opt.FlipV ? -1f : 1f;   // bitangent sign when V is flipped

            List<float> allVertices = new();
            List<float> allNormals = new();
            List<float> allTangents = new();
            List<float> allColors = new();
            List<float>[] allUV = new List<float>[UV_CHANNELS];
            for (int c = 0; c < UV_CHANNELS; c++) allUV[c] = new List<float>();
            List<uint> allIndices = new();
            List<SubMesh> subMeshes = new();

            uint totalVertexCount = 0;
            bool hasNormals = false, hasTangents = false, hasColors = false;
            bool[] hasUV = new bool[UV_CHANNELS];

            foreach (var m in scene.Meshes)
            {
                totalVertexCount += (uint)m.VertexCount;
                if (m.HasNormals) hasNormals = true;
                if (m.HasTangentBasis) hasTangents = true;
                if (m.HasVertexColors(0)) hasColors = true;
                for (int c = 0; c < UV_CHANNELS; c++)
                    if (m.HasTextureCoords(c)) hasUV[c] = true;
            }

            bool use32Bit = totalVertexCount > 65535;
            targetMesh.m_Use16BitIndices = !use32Bit;

            uint globalVertexAccum = 0;
            uint globalIndexAccum = 0;

            for (int meshIdx = 0; meshIdx < scene.Meshes.Count; meshIdx++)
            {
                var mesh = scene.Meshes[meshIdx];
                int vCount = mesh.VertexCount;

                handler.Report(40 + (int)(((float)meshIdx / scene.Meshes.Count) * 35),
                    $"Processing SubMesh {meshIdx + 1}/{scene.Meshes.Count} ({vCount} verts)...");

                for (int v = 0; v < vCount; v++)
                {
                    var pos = mesh.Vertices[v];
                    allVertices.Add(sx * pos.X);
                    allVertices.Add(pos.Y);
                    allVertices.Add(pos.Z);

                    Vector3D n = mesh.HasNormals ? mesh.Normals[v] : new Vector3D(0, 1, 0);
                    if (hasNormals)
                    {
                        allNormals.Add(sx * n.X);
                        allNormals.Add(n.Y);
                        allNormals.Add(n.Z);
                    }

                    if (hasTangents)
                    {
                        Vector3D t = mesh.HasTangentBasis ? mesh.Tangents[v] : new Vector3D(1, 0, 0);
                        Vector3D b = mesh.HasTangentBasis ? mesh.BiTangents[v] : new Vector3D(0, 0, 1);

                        float nx = sx * n.X, ny = n.Y, nz = n.Z;
                        float tx = sx * t.X, ty = t.Y, tz = t.Z;
                        float bx = sx * b.X * sv, by = b.Y * sv, bz = b.Z * sv;

                        // handedness: w = dot(cross(n, t), b) < 0 ? -1 : 1
                        float cx = ny * tz - nz * ty;
                        float cy = nz * tx - nx * tz;
                        float cz = nx * ty - ny * tx;
                        float w = (cx * bx + cy * by + cz * bz) < 0f ? -1f : 1f;

                        allTangents.Add(tx);
                        allTangents.Add(ty);
                        allTangents.Add(tz);
                        allTangents.Add(w);
                    }

                    for (int c = 0; c < UV_CHANNELS; c++)
                    {
                        if (!hasUV[c]) continue;
                        var uv = mesh.HasTextureCoords(c) ? mesh.TextureCoordinateChannels[c][v] : new Vector3D(0, 0, 0);
                        allUV[c].Add(uv.X);
                        allUV[c].Add(opt.FlipV ? 1.0f - uv.Y : uv.Y);
                    }

                    if (hasColors)
                    {
                        var col = mesh.HasVertexColors(0) ? mesh.VertexColorChannels[0][v] : new Color4D(1, 1, 1, 1);
                        allColors.Add(col.R);
                        allColors.Add(col.G);
                        allColors.Add(col.B);
                        allColors.Add(col.A);
                    }
                }

                // Mirrored X flips triangle orientation, so swap winding only then.
                List<uint> subIndices = new();
                foreach (var face in mesh.Faces)
                {
                    if (face.IndexCount != 3) continue;

                    uint a = (uint)(face.Indices[0] + globalVertexAccum);
                    uint b = (uint)(face.Indices[opt.FlipX ? 2 : 1] + globalVertexAccum);
                    uint c = (uint)(face.Indices[opt.FlipX ? 1 : 2] + globalVertexAccum);

                    subIndices.Add(a); subIndices.Add(b); subIndices.Add(c);
                    allIndices.Add(a); allIndices.Add(b); allIndices.Add(c);
                }

                uint subIndexCount = (uint)subIndices.Count;
                subMeshes.Add(new SubMesh
                {
                    firstByte = globalIndexAccum * (use32Bit ? 4u : 2u),
                    indexCount = subIndexCount,
                    topology = MeshHelper.GfxPrimitiveType.Triangles,
                    triangleCount = subIndexCount / 3,
                    baseVertex = 0,
                    firstVertex = globalVertexAccum,
                    vertexCount = (uint)vCount,
                    localAABB = CalculateSubMeshAABB(allVertices, subIndices)
                });

                globalVertexAccum += (uint)vCount;
                globalIndexAccum += subIndexCount;
            }

            targetMesh.m_VertexCount = (int)totalVertexCount;
            targetMesh.m_Vertices = allVertices.ToArray();
            targetMesh.m_Normals = hasNormals ? allNormals.ToArray() : Array.Empty<float>();
            targetMesh.m_Tangents = hasTangents ? allTangents.ToArray() : Array.Empty<float>();
            targetMesh.m_Colors = hasColors ? allColors.ToArray() : Array.Empty<float>();
            for (int c = 0; c < UV_CHANNELS; c++)
                targetMesh.SetUV(c, hasUV[c] ? allUV[c].ToArray() : Array.Empty<float>());
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
                int i = (int)idx * 3;
                if (i + 2 >= vertices.Count) continue;
                float x = vertices[i], y = vertices[i + 1], z = vertices[i + 2];
                if (x < minX) minX = x; if (y < minY) minY = y; if (z < minZ) minZ = z;
                if (x > maxX) maxX = x; if (y > maxY) maxY = y; if (z > maxZ) maxZ = z;
            }

            return new AABB
            {
                m_Center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f),
                m_Extent = new Vector3((maxX - minX) * 0.5f, (maxY - minY) * 0.5f, (maxZ - minZ) * 0.5f)
            };
        }
    }
}