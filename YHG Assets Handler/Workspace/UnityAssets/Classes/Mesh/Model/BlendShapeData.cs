using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.Text;
using YHG_Assets_Handler.Utils.Unity;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public class BlendShapeData
    {
        public BlendShapeVertex[] vertices = Array.Empty<BlendShapeVertex>();
        public MeshBlendShape[] shapes = Array.Empty<MeshBlendShape>();
        public MeshBlendShapeChannel[] channels = Array.Empty<MeshBlendShapeChannel>();
        public float[] fullWeights = Array.Empty<float>();

        public BlendShapeData() { }

        public BlendShapeData(AssetTypeValueField field, UnityVersion version)
        {
            if (field.IsDummy) return;

            var vertsArray = field["vertices.Array"];
            if (vertsArray.IsDummy) vertsArray = field["m_ShapeVertices.Array"];

            if (!vertsArray.IsDummy)
            {
                int count = vertsArray.AsArray.size;
                vertices = new BlendShapeVertex[count];
                for (int i = 0; i < count; i++)
                    vertices[i] = new BlendShapeVertex(vertsArray[i]);
            }

            var shapesArray = field["shapes.Array"];
            if (shapesArray.IsDummy) shapesArray = field["m_Shapes.Array"];

            if (!shapesArray.IsDummy)
            {
                int count = shapesArray.AsArray.size;
                shapes = new MeshBlendShape[count];
                for (int i = 0; i < count; i++)
                    shapes[i] = new MeshBlendShape(shapesArray[i], version);
            }

            var channelsArray = field["channels.Array"];
            if (!channelsArray.IsDummy)
            {
                int count = channelsArray.AsArray.size;
                channels = new MeshBlendShapeChannel[count];
                for (int i = 0; i < count; i++)
                    channels[i] = new MeshBlendShapeChannel(channelsArray[i]);
            }

            var weightsArray = field["fullWeights.Array"];
            if (!weightsArray.IsDummy)
            {
                int count = weightsArray.AsArray.size;
                fullWeights = new float[count];
                for (int i = 0; i < count; i++)
                    fullWeights[i] = weightsArray[i].AsFloat;
            }
        }

        public void WriteTo(AssetTypeValueField field, UnityVersion version)
        {
            if (field.IsDummy) return;

            var vertsArray = field["vertices.Array"];
            if (vertsArray.IsDummy) vertsArray = field["m_ShapeVertices.Array"];

            if (!vertsArray.IsDummy)
            {
                vertsArray.Children.Clear();
                foreach (var v in vertices)
                {
                    var slot = vertsArray.GetEmptyArrayField() ?? ValueBuilder.DefaultValueFieldFromArrayTemplate(vertsArray);
                    if (slot != null && !slot.IsDummy)
                    {
                        v.WriteTo(slot);
                        vertsArray.Children.Add(slot);
                    }
                }
            }

            var shapesArray = field["shapes.Array"];
            if (shapesArray.IsDummy) shapesArray = field["m_Shapes.Array"];

            if (!shapesArray.IsDummy)
            {
                shapesArray.Children.Clear();
                foreach (var s in shapes)
                {
                    var slot = shapesArray.GetEmptyArrayField() ?? ValueBuilder.DefaultValueFieldFromArrayTemplate(shapesArray);
                    if (slot != null && !slot.IsDummy)
                    {
                        s.WriteTo(slot, version);
                        shapesArray.Children.Add(slot);
                    }
                }
            }

            var channelsArray = field["channels.Array"];
            if (!channelsArray.IsDummy)
            {
                channelsArray.Children.Clear();
                foreach (var c in channels)
                {
                    var slot = channelsArray.GetEmptyArrayField() ?? ValueBuilder.DefaultValueFieldFromArrayTemplate(channelsArray);
                    if (slot != null && !slot.IsDummy)
                    {
                        c.WriteTo(slot);
                        channelsArray.Children.Add(slot);
                    }
                }
            }

            var weightsArray = field["fullWeights.Array"];
            if (!weightsArray.IsDummy)
            {
                weightsArray.Children.Clear();
                foreach (var w in fullWeights)
                {
                    var slot = weightsArray.GetEmptyArrayField() ?? ValueBuilder.DefaultValueFieldFromArrayTemplate(weightsArray);
                    if (slot != null && !slot.IsDummy)
                    {
                        slot.AsFloat = w;
                        weightsArray.Children.Add(slot);
                    }
                }
            }
        }
    }
}
