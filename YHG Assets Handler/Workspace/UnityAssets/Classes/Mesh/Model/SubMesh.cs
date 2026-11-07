using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public class SubMesh
    {
        public uint firstByte;
        public uint indexCount;
        public MeshHelper.GfxPrimitiveType topology;
        public uint triangleCount;
        public uint baseVertex;
        public uint firstVertex;
        public uint vertexCount;
        public AABB? localAABB;

        public SubMesh() { }

        public SubMesh(AssetTypeValueField smField, UnityVersion version)
        {
            if (smField.IsDummy) return;

            var f = smField["firstByte"];
            if (!f.IsDummy) firstByte = f.AsUInt;

            f = smField["indexCount"];
            if (!f.IsDummy) indexCount = f.AsUInt;

            f = smField["topology"];
            if (!f.IsDummy) topology = (MeshHelper.GfxPrimitiveType)f.AsInt;

            if (version.major < 4)
            {
                var triField = smField["triangleCount"];
                if (!triField.IsDummy) triangleCount = triField.AsUInt;
            }

            if (version.major > 2017 || (version.major == 2017 && version.minor >= 3))
            {
                var baseVertField = smField["baseVertex"];
                if (!baseVertField.IsDummy) baseVertex = baseVertField.AsUInt;
            }

            if (version.major >= 3)
            {
                var firstVertField = smField["firstVertex"];
                if (!firstVertField.IsDummy) firstVertex = firstVertField.AsUInt;

                var vertCountField = smField["vertexCount"];
                if (!vertCountField.IsDummy) vertexCount = vertCountField.AsUInt;

                var aabbField = smField["localAABB"];
                if (!aabbField.IsDummy) localAABB = new AABB(aabbField);
            }
        }

        public void WriteTo(AssetTypeValueField smField, UnityVersion version)
        {
            if (smField.IsDummy) return;

            var f = smField["firstByte"];
            if (!f.IsDummy) f.AsUInt = firstByte;

            f = smField["indexCount"];
            if (!f.IsDummy) f.AsUInt = indexCount;

            f = smField["topology"];
            if (!f.IsDummy) f.AsInt = (int)topology;

            if (version.major < 4)
            {
                var triField = smField["triangleCount"];
                if (!triField.IsDummy) triField.AsUInt = triangleCount;
            }

            if (version.major > 2017 || (version.major == 2017 && version.minor >= 3))
            {
                var baseVertField = smField["baseVertex"];
                if (!baseVertField.IsDummy) baseVertField.AsUInt = baseVertex;
            }

            if (version.major >= 3)
            {
                var firstVertField = smField["firstVertex"];
                if (!firstVertField.IsDummy) firstVertField.AsUInt = firstVertex;

                var vertCountField = smField["vertexCount"];
                if (!vertCountField.IsDummy) vertCountField.AsUInt = vertexCount;

                var aabbField = smField["localAABB"];
                if (!aabbField.IsDummy && localAABB != null)
                {
                    localAABB.WriteTo(aabbField);
                }
            }
        }
    }
}
