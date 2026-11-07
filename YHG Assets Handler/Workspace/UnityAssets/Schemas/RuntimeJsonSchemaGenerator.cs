using AssetsTools.NET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Reflection;
using YHG_Assets_Handler.Utils;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Schemas
{
    public static class RuntimeJsonSchemaGenerator
    {
        private const int MaxRecursionDepth = 12;
        private static readonly string UniversalBaseDirectory = Path.Combine(AppPathResolver.WorkflowDir, "schemas", "unity");
        private static readonly string MonoCacheDirectory = Path.Combine(UniversalBaseDirectory, "cache", "monobehaviour");
        public static string EnsureAndGetSchemaUri(
            string unityVersion,
            AssetTypeTemplateField template,
            string? scriptIdentifier = null)
        {
            bool isMonoBehaviour = template.Type == "MonoBehaviour" && !string.IsNullOrWhiteSpace(scriptIdentifier);
            string targetFolder;
            string fileName;
            if (isMonoBehaviour)
            {
                targetFolder = MonoCacheDirectory;
                fileName = $"{SanitizeFileName(scriptIdentifier!)}.schema.json";
            }
            else
            {
                string cleanVersion = string.IsNullOrWhiteSpace(unityVersion) ? "Universal" : SanitizeFileName(unityVersion.Trim());
                targetFolder = Path.Combine(UniversalBaseDirectory, cleanVersion);
                fileName = $"{SanitizeFileName(template.Type)}.schema.json";
            }
            Directory.CreateDirectory(targetFolder);
            string fullPath = Path.Combine(targetFolder, fileName);
            if (!File.Exists(fullPath))
            {
                string schemaJson = GenerateSchema(template, unityVersion);
                string tempPath = Path.Combine(targetFolder, $"{Guid.NewGuid():N}.tmp");
                File.WriteAllText(tempPath, schemaJson);
                try
                {
                    File.Move(tempPath, fullPath);
                }
                catch (IOException)
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
            }
            return new Uri(fullPath).AbsoluteUri;
        }
        public static string GenerateSchema(AssetTypeTemplateField rootTemplate, string unityVersion)
        {
            var rootSchema = new JObject
            {
                ["$schema"] = "http://json-schema.org/draft-07/schema#",
                ["title"] = $"{rootTemplate.Type} ({unityVersion})",
                ["type"] = "object",
                ["additionalProperties"] = false
            };
            var properties = new JObject();
            var requiredList = new JArray();
            properties["$schema"] = new JObject
            {
                ["type"] = "string",
                ["description"] = "URI pointing to this JSON Schema definition"
            };
            foreach (AssetTypeTemplateField child in rootTemplate.Children)
            {
                properties[child.Name] = ConvertFieldToSchemaNode(child, 0);
                requiredList.Add(child.Name);
            }
            rootSchema["properties"] = properties;
            rootSchema["required"] = requiredList;
            return rootSchema.ToString(Formatting.Indented);
        }
        private static JObject ConvertFieldToSchemaNode(AssetTypeTemplateField field, int depth)
        {
            if (depth > MaxRecursionDepth)
            {
                return new JObject
                {
                    ["type"] = "object",
                    ["description"] = "Max schema recursion depth reached."
                };
            }
            if (field.ValueType == AssetValueType.String || field.Type == "string")
            {
                return new JObject
                {
                    ["type"] = "string",
                    ["description"] = "Unity Primitive: string"
                };
            }
            if (field.HasValue && !field.IsArray && field.Children.Count == 0)
            {
                return MapPrimitiveType(field.ValueType, field.Type);
            }
            if (field.IsArray)
            {
                return BuildArraySchemaNode(field, depth);
            }
            if (field.Children.Count == 1 && field.Children[0].Name == "Array" && field.Children[0].IsArray)
            {
                var wrapperObj = new JObject
                {
                    ["type"] = "object",
                    ["description"] = $"Unity Vector: {field.Type}",
                    ["additionalProperties"] = false
                };
                var wrapperProps = new JObject
                {
                    ["Array"] = BuildArraySchemaNode(field.Children[0], depth + 1)
                };
                wrapperObj["properties"] = wrapperProps;
                wrapperObj["required"] = new JArray { "Array" };
                wrapperObj["defaultSnippets"] = new JArray
                {
                    new JObject
                    {
                        ["label"] = $"Default {field.Type}",
                        ["description"] = $"Inserts {field.Type} with empty Array",
                        ["body"] = new JObject
                        {
                            ["Array"] = new JArray()
                        }
                    }
                };
                return wrapperObj;
            }
            var objNode = new JObject
            {
                ["type"] = "object",
                ["description"] = $"Unity Type: {field.Type}",
                ["additionalProperties"] = false
            };
            var childProperties = new JObject();
            var childRequired = new JArray();
            var snippetBody = new JObject();
            foreach (AssetTypeTemplateField child in field.Children)
            {
                childProperties[child.Name] = ConvertFieldToSchemaNode(child, depth + 1);
                childRequired.Add(child.Name);
                snippetBody[child.Name] = GetDefaultValueForField(child);
            }
            objNode["properties"] = childProperties;
            if (childRequired.Count > 0)
            {
                objNode["required"] = childRequired;
            }
            if (snippetBody.Count > 0)
            {
                objNode["defaultSnippets"] = new JArray
                {
                    new JObject
                    {
                        ["label"] = $"Complete {field.Type}",
                        ["description"] = $"Inserts default {field.Type} structure",
                        ["body"] = snippetBody
                    }
                };
            }
            return objNode;
        }
        private static JObject BuildArraySchemaNode(AssetTypeTemplateField arrayField, int depth)
        {
            var arrayNode = new JObject
            {
                ["type"] = "array",
                ["description"] = $"Unity Array: {arrayField.Type}[]"
            };
            AssetTypeTemplateField? itemTemplate = null;
            if (arrayField.Children.Count >= 2 && arrayField.Children[0].Name == "size")
            {
                itemTemplate = arrayField.Children[1];
            }
            else if (arrayField.Children.Count > 1)
            {
                itemTemplate = arrayField.Children[1];
            }
            else if (arrayField.Children.Count == 1)
            {
                itemTemplate = arrayField.Children[0];
            }
            if (itemTemplate != null)
            {
                arrayNode["items"] = ConvertFieldToSchemaNode(itemTemplate, depth + 1);
                var itemSnippet = GetDefaultValueForField(itemTemplate);
                if (itemSnippet is JObject or JValue)
                {
                    arrayNode["defaultSnippets"] = new JArray
                    {
                        new JObject
                        {
                            ["label"] = $"New {itemTemplate.Name}",
                            ["description"] = $"Inserts standard {itemTemplate.Type} item",
                            ["body"] = itemSnippet
                        }
                    };
                }
            }
            else
            {
                arrayNode["items"] = new JObject { ["type"] = "object" };
            }
            return arrayNode;
        }
        private static JToken GetDefaultValueForField(AssetTypeTemplateField field)
        {
            if (field.ValueType == AssetValueType.String || field.Type == "string")
            {
                return "";
            }
            if (field.Children.Count == 1 && field.Children[0].Name == "Array" && field.Children[0].IsArray)
            {
                return new JObject
                {
                    ["Array"] = new JArray()
                };
            }
            if (field.IsArray)
            {
                return new JArray();
            }
            if (field.HasValue && field.Children.Count == 0)
            {
                return field.ValueType switch
                {
                    AssetValueType.Bool => false,
                    AssetValueType.Float or AssetValueType.Double => 0.0,
                    _ => 0
                };
            }
            var nestedObj = new JObject();
            foreach (var subChild in field.Children)
            {
                nestedObj[subChild.Name] = GetDefaultValueForField(subChild);
            }
            return nestedObj;
        }
        private static JObject MapPrimitiveType(AssetValueType valueType, string typeName)
        {
            var node = new JObject
            {
                ["description"] = $"Unity Primitive: {typeName}"
            };
            switch (valueType)
            {
                case AssetValueType.Bool:
                    node["type"] = "boolean";
                    break;
                case AssetValueType.UInt8:
                    node["type"] = "integer";
                    node["minimum"] = 0;
                    node["maximum"] = 255;
                    break;
                case AssetValueType.Int8:
                    node["type"] = "integer";
                    node["minimum"] = -128;
                    node["maximum"] = 127;
                    break;
                case AssetValueType.UInt16:
                    node["type"] = "integer";
                    node["minimum"] = 0;
                    node["maximum"] = 65535;
                    break;
                case AssetValueType.Int16:
                    node["type"] = "integer";
                    node["minimum"] = -32768;
                    node["maximum"] = 32767;
                    break;
                case AssetValueType.UInt32:
                case AssetValueType.Int32:
                case AssetValueType.UInt64:
                case AssetValueType.Int64:
                    node["type"] = "integer";
                    break;
                case AssetValueType.Float:
                case AssetValueType.Double:
                    node["type"] = "number";
                    break;
                case AssetValueType.String:
                    node["type"] = "string";
                    break;
                default:
                    node["type"] = "string";
                    break;
            }
            return node;
        }
        public static void ClearMonoCache()
        {
            try
            {
                if (Directory.Exists(MonoCacheDirectory))
                {
                    Directory.Delete(MonoCacheDirectory, recursive: true);
                }
            }
            catch
            {
            }
        }
        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}