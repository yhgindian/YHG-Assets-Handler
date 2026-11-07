using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;

namespace YHG_Assets_Handler.Workspace.UnityAssets
{
    public class AssetImportExport : IDisposable
    {
        private readonly Stream _stream;
        private readonly StreamWriter? _streamWriter;
        private readonly StreamReader? _streamReader;
        private readonly bool _leaveOpen;
        private bool _disposed;

        public AssetImportExport(Stream stream, bool leaveOpen = false)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _leaveOpen = leaveOpen;

            if (_stream.CanWrite)
            {
                _streamWriter = new StreamWriter(_stream, Encoding.UTF8, 8192, leaveOpen: true);
            }

            if (_stream.CanRead)
            {
                _streamReader = new StreamReader(_stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 8192, leaveOpen: true);
            }
        }

        public AssetImportExport(StreamWriter writer, bool leaveOpen = false)
        {
            _streamWriter = writer ?? throw new ArgumentNullException(nameof(writer));
            _stream = writer.BaseStream;
            _leaveOpen = leaveOpen;
        }

        public AssetImportExport(StreamReader reader, bool leaveOpen = false)
        {
            _streamReader = reader ?? throw new ArgumentNullException(nameof(reader));
            _stream = reader.BaseStream;
            _leaveOpen = leaveOpen;
        }

        #region Raw Dump & Import

        public void DumpRawAsset(AssetsFileReader reader, long position, uint size)
        {
            var assetFs = reader.BaseStream;
            assetFs.Position = position;

            byte[] buf = new byte[8192];
            long bytesLeft = size;

            while (bytesLeft > 0)
            {
                int toRead = (int)Math.Min(bytesLeft, buf.Length);
                int readSize = assetFs.Read(buf, 0, toRead);
                if (readSize == 0) break;

                _stream.Write(buf, 0, readSize);
                bytesLeft -= readSize;
            }

            _stream.Flush();
        }

        public byte[] ImportRawAsset()
        {
            if (_stream.CanSeek)
            {
                _stream.Position = 0;
            }

            using var ms = new MemoryStream((int)(_stream.CanSeek ? _stream.Length : 0));
            _stream.CopyTo(ms);
            return ms.ToArray();
        }

        #endregion

        #region JSON Dump (Export)

        public void DumpJsonAsset(AssetTypeValueField baseField, bool indent = true, string? schemaUri = null)
        {
            if (_streamWriter == null)
            {
                throw new InvalidOperationException("The underlying stream does not support writing.");
            }

            var jBaseField = RecurseJsonDump(baseField);

            if (!string.IsNullOrEmpty(schemaUri) && jBaseField is JObject jObj)
            {
                jObj.AddFirst(new JProperty("$schema", schemaUri));
            }

            using var jsonWriter = new JsonTextWriter(_streamWriter)
            {
                Formatting = indent ? Formatting.Indented : Formatting.None,
                CloseOutput = false
            };

            jBaseField.WriteTo(jsonWriter);
            jsonWriter.Flush();
            _streamWriter.Flush();
        }

        private JToken RecurseJsonDump(AssetTypeValueField field)
        {
            var template = field.TemplateField;

            if (template.IsArray)
            {
                var jArray = new JArray();
                if (template.ValueType != AssetValueType.ByteArray)
                {
                    int childCount = field.Children.Count;
                    for (int i = 0; i < childCount; i++)
                    {
                        jArray.Add(RecurseJsonDump(field.Children[i]));
                    }
                }
                else
                {
                    byte[] byteArrayData = field.AsByteArray;
                    for (int i = 0; i < byteArrayData.Length; i++)
                    {
                        jArray.Add(byteArrayData[i]);
                    }
                }

                return jArray;
            }

            if (field.Value != null)
            {
                var valueType = field.Value.ValueType;

                if (valueType != AssetValueType.ManagedReferencesRegistry)
                {
                    object value = valueType switch
                    {
                        AssetValueType.Bool => field.AsBool,
                        AssetValueType.Int8 or
                        AssetValueType.Int16 or
                        AssetValueType.Int32 => field.AsInt,
                        AssetValueType.Int64 => field.AsLong,
                        AssetValueType.UInt8 or
                        AssetValueType.UInt16 or
                        AssetValueType.UInt32 => field.AsUInt,
                        AssetValueType.UInt64 => field.AsULong,
                        AssetValueType.String => field.AsString,
                        AssetValueType.Float => field.AsFloat,
                        AssetValueType.Double => field.AsDouble,
                        _ => string.Empty
                    };

                    return new JValue(value);
                }

                return JsonDumpManagedReferencesRegistry(field);
            }

            var jObject = new JObject();
            foreach (AssetTypeValueField child in field)
            {
                jObject.Add(child.FieldName, RecurseJsonDump(child));
            }

            return jObject;
        }

        private JObject JsonDumpManagedReferencesRegistry(AssetTypeValueField field)
        {
            var registry = field.Value.AsManagedReferencesRegistry;

            if (registry.version != 1 && registry.version != 2)
            {
                throw new NotSupportedException($"ManagedReferencesRegistry version {registry.version} is not supported.");
            }

            var jArrayRefs = new JArray();
            foreach (var refObj in registry.references)
            {
                var typeRef = refObj.type;

                var jObjManagedType = new JObject
                {
                    ["class"] = typeRef.ClassName,
                    ["ns"] = typeRef.Namespace,
                    ["asm"] = typeRef.AsmName
                };

                var jObjData = new JObject();
                foreach (var child in refObj.data)
                {
                    jObjData.Add(child.FieldName, RecurseJsonDump(child));
                }

                var jObjRefObject = new JObject
                {
                    ["type"] = jObjManagedType,
                    ["data"] = jObjData
                };

                if (registry.version != 1)
                {
                    jObjRefObject.AddFirst(new JProperty("rid", refObj.rid));
                }

                jArrayRefs.Add(jObjRefObject);
            }

            return new JObject
            {
                ["version"] = registry.version,
                ["RefIds"] = jArrayRefs
            };
        }

        #endregion

        #region JSON Import

        public byte[]? ImportJsonAsset(
            AssetTypeTemplateField tempField,
            RefTypeManager? refMan,
            out string? exceptionMessage)
        {
            if (_streamReader == null)
            {
                exceptionMessage = "Underlying stream does not support reading.";
                return null;
            }

            using var ms = new MemoryStream();
            var writer = new AssetsFileWriter(ms)
            {
                BigEndian = false
            };

            try
            {
                using var jsonReader = new JsonTextReader(_streamReader)
                {
                    CloseInput = false
                };

                var token = JToken.ReadFrom(jsonReader);
                if (token is JObject jObj)
                    jObj.Remove("$schema");
                RecurseJsonImport(writer, tempField, token, refMan);

                exceptionMessage = null;
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                exceptionMessage = ex.Message;
                return null;
            }
        }

        private void RecurseJsonImport(
            AssetsFileWriter writer,
            AssetTypeTemplateField tempField,
            JToken token,
            RefTypeManager? refMan)
        {
            bool align = tempField.IsAligned;

            if (tempField.Children.Count == 1 && tempField.Children[0].IsArray && token.Type == JTokenType.Array)
            {
                RecurseJsonImport(writer, tempField.Children[0], token, refMan);
                return;
            }

            if (!tempField.HasValue && !tempField.IsArray)
            {
                foreach (AssetTypeTemplateField childTempField in tempField.Children)
                {
                    JToken? childToken = token[childTempField.Name];
                    if (childToken == null)
                    {
                        throw new InvalidDataException(
                            $"Missing field '{childTempField.Name}' in JSON. Parent field: {tempField.Type} {tempField.Name}.");
                    }

                    RecurseJsonImport(writer, childTempField, childToken, refMan);
                }

                if (align)
                {
                    writer.Align();
                }
            }
            else if (tempField.HasValue && tempField.ValueType == AssetValueType.ManagedReferencesRegistry)
            {
                JsonImportManagedReferencesRegistry(writer, tempField, token, refMan);
            }
            else
            {
                switch (tempField.ValueType)
                {
                    case AssetValueType.Bool:
                        writer.Write((bool)token);
                        break;
                    case AssetValueType.UInt8:
                        writer.Write((byte)token);
                        break;
                    case AssetValueType.Int8:
                        writer.Write((sbyte)token);
                        break;
                    case AssetValueType.UInt16:
                        writer.Write((ushort)token);
                        break;
                    case AssetValueType.Int16:
                        writer.Write((short)token);
                        break;
                    case AssetValueType.UInt32:
                        writer.Write((uint)token);
                        break;
                    case AssetValueType.Int32:
                        writer.Write((int)token);
                        break;
                    case AssetValueType.UInt64:
                        writer.Write((ulong)token);
                        break;
                    case AssetValueType.Int64:
                        writer.Write((long)token);
                        break;
                    case AssetValueType.Float:
                        writer.Write((float)token);
                        break;
                    case AssetValueType.Double:
                        writer.Write((double)token);
                        break;
                    case AssetValueType.String:
                        align = true;
                        writer.WriteCountStringInt32((string?)token ?? string.Empty);
                        break;
                    case AssetValueType.ByteArray:
                        var byteArrayJArray = (JArray?)token ?? [];
                        byte[] byteArrayData = new byte[byteArrayJArray.Count];
                        for (int i = 0; i < byteArrayJArray.Count; i++)
                        {
                            byteArrayData[i] = (byte)byteArrayJArray[i];
                        }
                        writer.Write(byteArrayData.Length);
                        writer.Write(byteArrayData);
                        break;
                }

                if (tempField.IsArray && tempField.ValueType != AssetValueType.ByteArray)
                {
                    AssetTypeTemplateField childTempField = tempField.Children[1];
                    var tokenArray = (JArray?)token
                        ?? throw new InvalidDataException($"Field '{tempField.Name}' is marked as array but not array in JSON.");

                    writer.Write(tokenArray.Count);
                    foreach (JToken childToken in tokenArray.Children())
                    {
                        RecurseJsonImport(writer, childTempField, childToken, refMan);
                    }
                }

                if (align)
                {
                    writer.Align();
                }
            }
        }

        private void JsonImportManagedReferencesRegistry(
            AssetsFileWriter writer,
            AssetTypeTemplateField tempField,
            JToken token,
            RefTypeManager? refMan)
        {
            int version = (int)ExpectAndReadField(token, "version", tempField);
            if (version < 1 || version > 2)
            {
                throw new InvalidDataException($"ManagedReferencesRegistry version {version} is invalid.");
            }

            var refIdsArray = (JArray)ExpectAndReadField(token, "RefIds", tempField);
            writer.Write(version);

            int childCount = refIdsArray.Count;
            if (version != 1)
            {
                writer.Write(childCount);
            }

            for (int i = 0; i < childCount; i++)
            {
                JToken refdObjectToken = refIdsArray[i];
                long rid = (long)ExpectAndReadField(refdObjectToken, "rid", tempField);
                if (version == 1)
                {
                    if (rid != i)
                    {
                        throw new InvalidDataException($"Field rid must be consecutive. Expected {i}, found {rid}.");
                    }
                }
                else
                {
                    writer.Write(rid);
                }

                JToken typeToken = ExpectAndReadField(refdObjectToken, "type", tempField);
                var typeRef = new AssetTypeReference
                {
                    ClassName = (string?)ExpectAndReadField(typeToken, "class", tempField) ?? string.Empty,
                    Namespace = (string?)ExpectAndReadField(typeToken, "ns", tempField) ?? string.Empty,
                    AsmName = (string?)ExpectAndReadField(typeToken, "asm", tempField) ?? string.Empty
                };

                JToken dataToken = ExpectAndReadField(refdObjectToken, "data", tempField);
                typeRef.WriteAsset(writer);

                if (string.IsNullOrEmpty(typeRef.ClassName) &&
                    string.IsNullOrEmpty(typeRef.Namespace) &&
                    string.IsNullOrEmpty(typeRef.AsmName))
                {
                    continue;
                }

                if (refMan == null)
                {
                    throw new InvalidOperationException("RefTypeManager is required to deserialize ManagedReferencesRegistry objects.");
                }

                AssetTypeTemplateField? objectTempField = refMan.GetTemplateField(typeRef);
                if (objectTempField == null)
                {
                    throw new InvalidDataException(
                        $"Failed to resolve managed reference: {typeRef.Namespace}.{typeRef.ClassName} in {typeRef.AsmName}.");
                }

                RecurseJsonImport(writer, objectTempField, dataToken, refMan);
            }

            if (version == 1)
            {
                AssetTypeReference.TERMINUS.WriteAsset(writer);
            }
            else
            {
                writer.Align();
            }
        }

        private static JToken ExpectAndReadField(JToken token, string name, AssetTypeTemplateField? tempField)
        {
            var field = token[name];
            if (field == null)
            {
                string context = tempField != null ? $" Parent: {tempField.Type} {tempField.Name}" : string.Empty;
                throw new InvalidDataException($"Missing field '{name}' in JSON.{context}");
            }
            return field;
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (_disposed) return;

            _streamWriter?.Flush();

            if (!_leaveOpen)
            {
                _streamWriter?.Dispose();
                _streamReader?.Dispose();
                _stream.Dispose();
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}