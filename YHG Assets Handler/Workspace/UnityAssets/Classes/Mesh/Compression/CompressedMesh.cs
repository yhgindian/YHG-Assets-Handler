using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public class CompressedMesh
    {
        public PackedFloatVector? m_Vertices;
        public PackedFloatVector? m_UV;
        public PackedFloatVector? m_BindPoses;
        public PackedFloatVector? m_Normals;
        public PackedFloatVector? m_Tangents;
        public PackedIntVector? m_Weights;
        public PackedIntVector? m_NormalSigns;
        public PackedIntVector? m_TangentSigns;
        public PackedFloatVector? m_FloatColors;
        public PackedIntVector? m_BoneIndices;
        public PackedIntVector? m_Triangles;
        public PackedIntVector? m_Colors;
        public uint m_UVInfo;

        public CompressedMesh() { }

        public CompressedMesh(AssetTypeValueField cmField, UnityVersion version)
        {
            if (cmField.IsDummy) return;

            var f = cmField["m_Vertices"];
            if (!f.IsDummy) m_Vertices = new PackedFloatVector(f);

            f = cmField["m_UV"];
            if (!f.IsDummy) m_UV = new PackedFloatVector(f);

            if (version.major < 5)
            {
                f = cmField["m_BindPoses"];
                if (!f.IsDummy) m_BindPoses = new PackedFloatVector(f);
            }

            f = cmField["m_Normals"];
            if (!f.IsDummy) m_Normals = new PackedFloatVector(f);

            f = cmField["m_Tangents"];
            if (!f.IsDummy) m_Tangents = new PackedFloatVector(f);

            f = cmField["m_Weights"];
            if (!f.IsDummy) m_Weights = new PackedIntVector(f);

            f = cmField["m_NormalSigns"];
            if (!f.IsDummy) m_NormalSigns = new PackedIntVector(f);

            f = cmField["m_TangentSigns"];
            if (!f.IsDummy) m_TangentSigns = new PackedIntVector(f);

            if (version.major >= 5)
            {
                f = cmField["m_FloatColors"];
                if (!f.IsDummy) m_FloatColors = new PackedFloatVector(f);
            }

            f = cmField["m_BoneIndices"];
            if (!f.IsDummy) m_BoneIndices = new PackedIntVector(f);

            f = cmField["m_Triangles"];
            if (!f.IsDummy) m_Triangles = new PackedIntVector(f);

            if (version.major > 3 || (version.major == 3 && version.minor >= 5))
            {
                if (version.major < 5)
                {
                    f = cmField["m_Colors"];
                    if (!f.IsDummy) m_Colors = new PackedIntVector(f);
                }
                else
                {
                    f = cmField["m_UVInfo"];
                    if (!f.IsDummy) m_UVInfo = f.AsUInt;
                }
            }
        }

        public void WriteTo(AssetTypeValueField cmField, UnityVersion version)
        {
            if (cmField.IsDummy) return;

            var f = cmField["m_Vertices"];
            if (!f.IsDummy && m_Vertices != null) m_Vertices.WriteTo(f);

            f = cmField["m_UV"];
            if (!f.IsDummy && m_UV != null) m_UV.WriteTo(f);

            if (version.major < 5)
            {
                f = cmField["m_BindPoses"];
                if (!f.IsDummy && m_BindPoses != null) m_BindPoses.WriteTo(f);
            }

            f = cmField["m_Normals"];
            if (!f.IsDummy && m_Normals != null) m_Normals.WriteTo(f);

            f = cmField["m_Tangents"];
            if (!f.IsDummy && m_Tangents != null) m_Tangents.WriteTo(f);

            f = cmField["m_Weights"];
            if (!f.IsDummy && m_Weights != null) m_Weights.WriteTo(f);

            f = cmField["m_NormalSigns"];
            if (!f.IsDummy && m_NormalSigns != null) m_NormalSigns.WriteTo(f);

            f = cmField["m_TangentSigns"];
            if (!f.IsDummy && m_TangentSigns != null) m_TangentSigns.WriteTo(f);

            if (version.major >= 5)
            {
                f = cmField["m_FloatColors"];
                if (!f.IsDummy && m_FloatColors != null) m_FloatColors.WriteTo(f);
            }

            f = cmField["m_BoneIndices"];
            if (!f.IsDummy && m_BoneIndices != null) m_BoneIndices.WriteTo(f);

            f = cmField["m_Triangles"];
            if (!f.IsDummy && m_Triangles != null) m_Triangles.WriteTo(f);

            if (version.major > 3 || (version.major == 3 && version.minor >= 5))
            {
                if (version.major < 5)
                {
                    f = cmField["m_Colors"];
                    if (!f.IsDummy && m_Colors != null) m_Colors.WriteTo(f);
                }
                else
                {
                    f = cmField["m_UVInfo"];
                    if (!f.IsDummy) f.AsUInt = m_UVInfo;
                }
            }
        }
    }
}
