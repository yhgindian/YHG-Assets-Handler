using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Views.Controls.Tree;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.Utils.Unity
{
    public sealed class UnityNodePayload
    {
        public AssetsFileInstance FromFile { get; set; }
        public int FileId { get; set; }
        public long PathId { get; set; }
        public bool IsLink { get; set; }
        public bool IsUnresolvedDep { get; set; }

        public UnityNodePayload(AssetsFileInstance fromFile, int fileId, long pathId)
        {
            FromFile = fromFile;
            FileId = fileId;
            PathId = pathId;
        }
    }

    public static class UnityTreeBuilder
    {
        private const int ArrayChunkSize = 200;

        public static FieldNode BuildRoot(UnityAssetWorkspace workspace, AssetContainer container)
        {
            var fileInst = container.FileInstance;
            if (fileInst == null)
                return FieldNode.Label("No assets file loaded.", Palette.Warning);

            AssetTypeValueField? baseField;
            lock (fileInst.LockReader)
            {
                baseField = workspace.GetBaseField(container);
            }

            if (baseField == null)
                return FieldNode.Label("Asset failed to deserialize.", Palette.Warning);

            var root = MakeFieldNode(baseField.TypeName, baseField.FieldName, "", "", "", false);
            root.SetChildren(BuildChildren(workspace, fileInst, baseField, container.PathId));
            root.IsExpanded = true;
            return root;
        }

        public static List<FieldNode> BuildChildren(UnityAssetWorkspace workspace, AssetsFileInstance fromFile, AssetTypeValueField field, long fromPathId)
        {
            var result = new List<FieldNode>();
            var children = field.Children;
            if (children.Count == 0) return result;

            var template = field.TemplateField;
            if (template.IsArray)
            {
                var sizeTemplate = template.Children[0];
                result.Add(MakeFieldNode(sizeTemplate.Type, sizeTemplate.Name, " = ", field.AsArray.size.ToString(), "", false));

                if (children.Count > ArrayChunkSize)
                {
                    for (int start = 0; start < children.Count; start += ArrayChunkSize)
                    {
                        int s = start;
                        int end = Math.Min(start + ArrayChunkSize, children.Count);
                        var range = FieldNode.Label($"[{s} .. {end - 1}]");
                        range.SetLazy(() => BuildArrayItems(workspace, fromFile, children, s, end, fromPathId));
                        result.Add(range);
                    }
                }
                else
                {
                    result.AddRange(BuildArrayItems(workspace, fromFile, children, 0, children.Count, fromPathId));
                }
            }
            else
            {
                foreach (var child in children)
                {
                    if (child == null) continue;
                    result.Add(BuildFieldNode(workspace, fromFile, child, fromPathId));
                }
            }

            string typeName = field.TypeName;
            if (typeName.StartsWith("PPtr<") && typeName.EndsWith('>'))
            {
                var fileIdField = field["m_FileID"];
                var pathIdField = field["m_PathID"];
                if (!fileIdField.IsDummy && !pathIdField.IsDummy)
                {
                    int fileId = fileIdField.AsInt;
                    long pathId = pathIdField.AsLong;

                    var payload = new UnityNodePayload(fromFile, fileId, pathId) { IsLink = true };
                    var link = new FieldNode
                    {
                        NameText = "[view asset]",
                        NameBrush = Palette.Link,
                        Weight = FontWeights.Bold,
                        NodeCursor = Cursors.Hand,
                        Tag = payload
                    };

                    if (pathId != 0)
                        link.SetLazy(() => BuildPPtrChildren(workspace, fromFile, fileId, pathId));

                    result.Add(link);
                }
            }

            return result;
        }

        public static List<FieldNode> BuildPPtrChildren(UnityAssetWorkspace workspace, AssetsFileInstance fromFile, int fileId, long pathId)
        {
            var cont = workspace.GetAssetContainer(fromFile, fileId, pathId);
            if (cont == null)
            {
                var payload = new UnityNodePayload(fromFile, fileId, pathId)
                {
                    IsLink = true,
                    IsUnresolvedDep = true
                };

                return new List<FieldNode>
                {
                    new FieldNode
                    {
                        NameText = "[Unresolved Dependency - Right click to resolve]",
                        NameBrush = Palette.Warning,
                        NodeCursor = Cursors.Hand,
                        Tag = payload
                    }
                };
            }

            var baseField = workspace.GetBaseField(cont);
            if (baseField == null)
                return new List<FieldNode> { FieldNode.Label("[failed to deserialize base field]", Palette.Warning) };

            var node = MakeFieldNode(baseField.TypeName, baseField.FieldName, "", "", "", false);
            node.SetChildren(BuildChildren(workspace, cont.FileInstance, baseField, pathId));
            node.IsExpanded = true;
            return new List<FieldNode> { node };
        }

        private static List<FieldNode> BuildArrayItems(UnityAssetWorkspace workspace, AssetsFileInstance fromFile, List<AssetTypeValueField> children, int start, int end, long fromPathId)
        {
            var list = new List<FieldNode>(end - start);
            for (int i = start; i < end; i++)
            {
                var child = children[i];
                if (child == null) continue;

                var indexNode = FieldNode.Label(i.ToString());
                indexNode.SetLazy(() => new List<FieldNode> { BuildFieldNode(workspace, fromFile, child, fromPathId) });
                list.Add(indexNode);
            }
            return list;
        }

        private static FieldNode BuildFieldNode(UnityAssetWorkspace workspace, AssetsFileInstance fromFile, AssetTypeValueField childField, long fromPathId)
        {
            string middle = "", value = "", comment = "";
            bool isString = false;

            if (childField.Value != null)
            {
                AssetValueType valueType = childField.Value.ValueType;
                if (valueType == AssetValueType.String)
                {
                    middle = " = ";
                    value = EscapeAndQuoteString(childField.AsString);
                    isString = true;
                }
                else if (1 <= (int)valueType && (int)valueType <= 11)
                {
                    middle = " = ";
                    value = childField.AsString;
                }
                else if (valueType == AssetValueType.Array)
                {
                    middle = $" (size {childField.Children.Count})";
                }
                else if (valueType == AssetValueType.ByteArray)
                {
                    byte[] bytes = childField.AsByteArray;
                    middle = $" (size {bytes.Length}) = ";

                    const int MaxPreview = 20;
                    int n = Math.Min(bytes.Length, MaxPreview);
                    var sb = new StringBuilder(n * 3 + 4);
                    for (int i = 0; i < n; i++)
                    {
                        if (i > 0) sb.Append(' ');
                        sb.Append(bytes[i].ToString("X2"));
                    }
                    if (bytes.Length > MaxPreview) sb.Append(" ...");
                    value = sb.ToString();
                }

                if (valueType == AssetValueType.Int32 && childField.TemplateField.Name == "m_FileID")
                {
                    int fileId = childField.AsInt;
                    if (fileId == 0)
                    {
                        comment = fromFile.parentBundle != null
                            ? $" ({fromFile.parentBundle.name}/{fromFile.name})"
                            : $" ({fromFile.name})";
                    }
                    else if (fileId > 0 && fromFile.file.Metadata.Externals.Count >= fileId)
                    {
                        comment = $" ({Path.GetFileName(fromFile.file.Metadata.Externals[fileId - 1].PathName)})";
                    }
                }
            }

            var node = MakeFieldNode(childField.TypeName, childField.FieldName, middle, value, comment, isString);

            if (childField.Children.Count > 0)
                node.SetLazy(() => BuildChildren(workspace, fromFile, childField, fromPathId));

            return node;
        }

        public static FieldNode MakeFieldNode(string typeName, string fieldName, string middle, string value, string comment, bool isString)
        {
            bool primitive = AssetTypeValueField.GetValueTypeByTypeName(typeName) != AssetValueType.None;
            return new FieldNode
            {
                TypeText = typeName,
                TypeBrush = primitive ? Palette.Primitive : Palette.Type,
                NameText = " " + fieldName,
                Middle = middle,
                Value = value,
                ValueBrush = isString ? Palette.String : Palette.Value,
                Comment = comment,
                Weight = FontWeights.SemiBold
            };
        }

        private static string EscapeAndQuoteString(string str)
            => str.Length > 1000 ? $"\"{str[..1000]}...\"" : $"\"{str}\"";
    }
}