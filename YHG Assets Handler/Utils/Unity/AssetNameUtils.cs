using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.Utils.Unity
{
    public static class AssetNameUtils
    {
        private static readonly ConcurrentDictionary<AssetsFileInstance, NameReadOptimization> _gameObjectNro = new();
        private static readonly ConcurrentDictionary<AssetsFileInstance, NameReadOptimization> _monoBehaviourNro = new();

        private readonly record struct TemplateKey(AssetsFileInstance File, int TypeId, ushort ScriptIdx);
        private static readonly ConcurrentDictionary<TemplateKey, AssetTypeTemplateField?> _templateCache = new();

        private static AssetTypeTemplateField? GetTemplateCached(AssetsManager manager, AssetsFileInstance fileInst, AssetContainer cont)
        {
            ushort script = 0xFFFF;
            if (cont.TypeId == (int)AssetClassID.MonoBehaviour || cont.TypeId < 0)
            {
                try { script = cont.GetScriptIndex(fileInst.file); } catch { }
            }

            var key = new TemplateKey(fileInst, cont.TypeId, script);
            if (_templateCache.TryGetValue(key, out var cached))
                return cached;

            AssetTypeTemplateField? t;
            try
            {
                t = manager.GetTemplateBaseField(fileInst, cont, AssetReadFlags.SkipMonoBehaviourFields);
            }
            catch
            {
                try
                {
                    t = fileInst.file.Metadata.TypeTreeEnabled
                        ? manager.GetTemplateBaseField(fileInst, cont, AssetReadFlags.ForceFromCldb)
                        : null;
                }
                catch { t = null; }
            }

            _templateCache[key] = t;
            return t;
        }

        public static void ClearTemplateCache(AssetsFileInstance? inst = null)
        {
            if (inst == null) { _templateCache.Clear(); return; }
            foreach (var k in _templateCache.Keys)
                if (k.File == inst) _templateCache.TryRemove(k, out _);
        }

        /// <summary>
        /// Fast-path name resolution during initialization without type template traversal or full deserialization.
        /// Falls back to formatted identifier ("{Type} #{PathId}") if unresolved or unsupported. Thread-safe via reader lock.
        /// </summary>
        public static void GetDisplayNameQuick(
            UnityAssetWorkspace workspace,
            AssetContainer cont,
            out string assetName,
            out string typeName,
            int maxLen = 120)
        {
            int id = cont.TypeId;
            typeName = id >= 0 ? (Enum.GetName((AssetClassID)id) ?? $"Type 0x{id:X}") : "MonoBehaviour";
            assetName = string.Empty;

            try
            {
                int classId = id < 0 ? (int)AssetClassID.MonoBehaviour : id;

                // Shader ka naam nested hai, isliye yahan skip (fallback naam milega)
                if (classId != (int)AssetClassID.Shader)
                {
                    GetLockObjAndReader(cont, out object lockObj, out AssetsFileReader reader, out long pos);
                    lock (lockObj)
                    {
                        reader.Position = pos;
                        int compSize = cont.FileInstance.file.Header.Version > 16 ? 12 : 16;
                        GetDisplayNameNative(reader, classId, compSize, out assetName);
                    }

                    if (assetName == "Unnamed Asset") assetName = string.Empty;

                    if (string.IsNullOrEmpty(assetName) && classId == (int)AssetClassID.MonoBehaviour)
                        assetName = GetMonoBehaviourNameFast(workspace, cont);
                }
            }
            catch { assetName = string.Empty; }

            if (string.IsNullOrWhiteSpace(assetName))
                assetName = $"{typeName} #{cont.PathId}";
            else if (assetName.Length > maxLen)
                assetName = assetName[..maxLen];
        }

        public static void GetDisplayNameFast(
            UnityAssetWorkspace workspace,
            AssetContainer cont,
            bool usePrefix,
            out string assetName,
            out string typeName,
            int maxLen = 120)
        {
            assetName = string.Empty;
            typeName = "Unknown";

            var manager = workspace.am;
            var fileInst = cont.FileInstance;

            var tempBaseField = GetTemplateCached(manager, fileInst, cont);

            if (tempBaseField == null)
            {
                var maybeClassId = (AssetClassID)cont.TypeId;
                typeName = Enum.GetName(maybeClassId) ?? $"Type 0x{cont.TypeId:X}";
            }
            else
            {
                typeName = tempBaseField.Type;
            }

            if (tempBaseField != null && tempBaseField.Children.Count > 0)
            {
                try
                {
                    // Fast path 1: m_Name pehla child hai
                    var firstChild = tempBaseField.Children[0];
                    if (firstChild.Name == "m_Name" && firstChild.Type == "string" && cont.TypeId != (int)AssetClassID.Shader)
                    {
                        GetLockObjAndReader(cont, out object lockObj, out AssetsFileReader reader, out long pos);
                        lock (lockObj)
                        {
                            reader.Position = pos;
                            assetName = reader.ReadCountStringInt32();
                        }

                        if (!string.IsNullOrEmpty(assetName))
                        {
                            FinalizeName(ref assetName, maxLen);
                            return;
                        }
                    }

                    var nro = NameReadOptimization.Unchecked;
                    RefTypeManager? refMan = null;
                    RefTypeManager GetRef() => refMan ??= manager.GetRefTypeManager(fileInst);

                    // Path 2: GameObject
                    if (cont.TypeId == (int)AssetClassID.GameObject)
                    {
                        if (!_gameObjectNro.TryGetValue(fileInst, out nro))
                            _gameObjectNro[fileInst] = nro = NameReadOptimization.Unchecked;

                        if (nro == NameReadOptimization.Unchecked)
                            _gameObjectNro[fileInst] = nro = GetGameObjectNro(tempBaseField);

                        if (nro == NameReadOptimization.UseOptimized)
                        {
                            var headerVer = fileInst.file.Header.Version;
                            GetLockObjAndReader(cont, out object lockObj, out AssetsFileReader reader, out long pos);

                            lock (lockObj)
                            {
                                reader.Position = pos;
                                int size = reader.ReadInt32();
                                int componentSize = headerVer >= 17 ? 0x0C : 0x10;
                                reader.Position += (long)size * componentSize;
                                reader.Position += 4;
                                string rawName = reader.ReadCountStringInt32();
                                assetName = usePrefix ? $"GameObject {rawName}" : rawName;
                            }

                            if (!string.IsNullOrEmpty(assetName))
                            {
                                FinalizeName(ref assetName, maxLen);
                                return;
                            }
                        }
                    }
                    // Path 3: MonoBehaviour
                    else if (cont.TypeId == (int)AssetClassID.MonoBehaviour || cont.TypeId < 0)
                    {
                        if (!_monoBehaviourNro.TryGetValue(fileInst, out nro))
                            _monoBehaviourNro[fileInst] = nro = NameReadOptimization.Unchecked;

                        if (nro == NameReadOptimization.Unchecked)
                            _monoBehaviourNro[fileInst] = nro = GetMonoBehaviourNro(tempBaseField);

                        if (nro == NameReadOptimization.UseOptimized)
                        {
                            GetLockObjAndReader(cont, out object lockObj, out AssetsFileReader reader, out long pos);
                            lock (lockObj)
                            {
                                reader.Position = pos + 0x1C;
                                assetName = reader.ReadCountStringInt32();
                            }

                            if (string.IsNullOrEmpty(assetName))
                            {
                                assetName = GetMonoBehaviourNameFast(workspace, cont);
                            }

                            if (!string.IsNullOrEmpty(assetName))
                            {
                                FinalizeName(ref assetName, maxLen);
                                return;
                            }
                        }
                    }
                    // Path 4: Shader
                    else if (cont.TypeId == (int)AssetClassID.Shader)
                    {
                        GetLockObjAndReader(cont, out object lockObj, out AssetsFileReader reader, out long pos);
                        lock (lockObj)
                        {
                            reader.Position = pos;
                            var iterator = new AssetTypeValueIterator(tempBaseField, reader, GetRef());
                            iterator.ReadNext();
                            iterator.ReadNext();

                            while (iterator.ReadNext())
                            {
                                if (iterator.TempField.Name == "m_Name" && iterator.TempField.Type == "string" &&
                                    iterator.TempFieldStack.Count >= 2 && iterator.TempFieldStack[1].Name == "m_ParsedForm")
                                {
                                    var valueField = iterator.ReadValueField();
                                    assetName = valueField.AsString;
                                    break;
                                }
                            }
                        }
                        nro = NameReadOptimization.UseOptimized;
                    }

                    // Fallback iterator
                    if (nro == NameReadOptimization.UseIterator)
                    {
                        GetLockObjAndReader(cont, out object lockObj, out AssetsFileReader reader, out long pos);
                        lock (lockObj)
                        {
                            reader.Position = pos;
                            var iterator = new AssetTypeValueIterator(tempBaseField, reader, GetRef());
                            while (iterator.ReadNext())
                            {
                                if (iterator.TempField.Name == "m_Name" && iterator.TempField.Type == "string")
                                {
                                    var valueField = iterator.ReadValueField();
                                    assetName = valueField.AsString;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    assetName = string.Empty;
                }
            }

            if (string.IsNullOrEmpty(assetName))
            {
                assetName = $"{typeName} #{cont.PathId}";
            }

            FinalizeName(ref assetName, maxLen);
        }

        public static string GetMonoBehaviourNameFast(UnityAssetWorkspace workspace, AssetContainer cont)
        {
            var manager = workspace.am;
            if (cont.TypeId != (int)AssetClassID.MonoBehaviour && cont.TypeId >= 0)
                return string.Empty;

            try
            {
                var scriptIdx = cont.GetScriptIndex(cont.FileInstance.file);
                if (scriptIdx == ushort.MaxValue)
                    return "MonoBehaviour";

                var scriptPtr = cont.FileInstance.file.Metadata.ScriptTypes[scriptIdx];
                var fileInst = cont.FileInstance;

                if (scriptPtr.FileId != 0)
                    fileInst = fileInst.GetDependency(manager, scriptPtr.FileId - 1);

                if (fileInst == null)
                    return "MonoBehaviour";

                AssetFileInfo? info = fileInst.file.GetAssetInfo(scriptPtr.PathId);
                if (info == null)
                    return "MonoBehaviour";

                lock (fileInst.LockReader)
                {
                    var reader = fileInst.file.Reader;
                    reader.Position = info.GetAbsoluteByteOffset(fileInst.file);
                    return reader.ReadCountStringInt32();
                }
            }
            catch
            {
                return "MonoBehaviour";
            }
        }

        private static void GetLockObjAndReader(
            AssetContainer cont,
            out object lockObj,
            out AssetsFileReader reader,
            out long pos)
        {
            var fileInst = cont.FileInstance;

            if (cont.IsReplacerPreviewable && cont.Replacer != null)
            {
                var stream = cont.Replacer.GetPreviewStream();
                lockObj = stream;
                reader = new AssetsFileReader(stream);
                pos = 0;
            }
            else
            {
                lockObj = fileInst.LockReader;
                reader = cont.FileReader;
                pos = cont.AbsoluteByteStart;
            }
        }

        private static NameReadOptimization GetGameObjectNro(AssetTypeTemplateField tempField)
        {
            var b = tempField.Children;
            if (b.Count < 3) return NameReadOptimization.UseIterator;

            var b_m_Component = b[0];
            if (b_m_Component.Type != "vector" || b_m_Component.Children.Count != 1)
                return NameReadOptimization.UseIterator;

            var b_m_Component_Array = b_m_Component[0];
            if (b_m_Component_Array.Type != "Array" || b_m_Component_Array.Children.Count != 2)
                return NameReadOptimization.UseIterator;

            var b_m_Component_Array_data = b_m_Component_Array[1];
            if (b_m_Component_Array_data.Type != "ComponentPair" || b_m_Component_Array_data.Children.Count != 1)
                return NameReadOptimization.UseIterator;

            var b_m_Component_Array_data_component = b_m_Component_Array_data[0];
            if (b_m_Component_Array_data_component.Type != "PPtr<Component>" || b_m_Component_Array_data_component.Children.Count != 2)
                return NameReadOptimization.UseIterator;

            if (b[1].Type != "unsigned int" || b[2].Type != "string")
                return NameReadOptimization.UseIterator;

            return NameReadOptimization.UseOptimized;
        }

        private static NameReadOptimization GetMonoBehaviourNro(AssetTypeTemplateField tempField)
        {
            var b = tempField.Children;
            if (b.Count < 4) return NameReadOptimization.UseIterator;

            if (b[0].Type != "PPtr<GameObject>" || b[0].Children.Count != 2)
                return NameReadOptimization.UseIterator;

            if (b[1].Type != "UInt8")
                return NameReadOptimization.UseIterator;

            if (b[2].Type != "PPtr<MonoScript>" || b[2].Children.Count != 2)
                return NameReadOptimization.UseIterator;

            if (b[3].Type != "string")
                return NameReadOptimization.UseIterator;

            return NameReadOptimization.UseOptimized;
        }

        private static void FinalizeName(ref string name, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Unnamed asset";
                return;
            }

            if (name.Length > maxLen)
            {
                name = name[..maxLen];
            }
        }

        public static void GetDisplayNameNative(AssetContainer cont, out string name)
        {
            var reader = cont.FileReader;
            var position = cont.AbsoluteByteStart;
            reader.Position = position;
            GetDisplayNameNative(reader, cont.TypeId, cont.FileInstance.file.Header.Version > 16 ? 12 : 16, out name);
        }

        public static void GetDisplayNameNative(AssetsFileReader reader, int classID, int componentSize, out string name)
        {
            if (AssetsHelperExtra.HasName(classID))
            {
                name = reader.ReadCountStringInt32();
            }
            else if (classID == 1) // gameObject
            {
                var size = reader.ReadInt32();
                reader.Position += size * componentSize;
                reader.Position += 4;
                name = reader.ReadCountStringInt32();
            }
            else if (classID == 114) //MonoBehaviour
            {
                reader.Position += 28;
                name = reader.ReadCountStringInt32();
            }
            else
            {
                name = string.Empty;
            }
            if (name == "" || name == string.Empty) name = "Unnamed Asset";
        }

        public static string getSaveFileName(AssetContainer cont, string? name = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                GetDisplayNameNative(cont, out name);
            }

            name = CleanFileName(name);
            var fName = cont.FileInstance.name;
            var path = cont.PathId;

            var bundle = string.Empty;
            if (cont.FileInstance.parentBundle != null)
            {
                bundle = $"{cont.FileInstance.parentBundle.name}-";
            }

            return $"{bundle}{name}-{fName}-{path}";
        }

        public static string getSaveFileName(UnityAssetObjectModel aom)
        {
            return getSaveFileName(aom.assetContainer!, aom.Name);
        }

        private static string CleanFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }

        private enum NameReadOptimization
        {
            Unchecked,
            UseOptimized,
            UseIterator
        }
    }

    public static class AssetsHelperExtra
    {
        private static readonly HashSet<uint> Allowed = new()
        {
            21U, 28U, 43U, 48U, 49U, 62U, 72U, 74U, 83U, 84U, 86U, 89U, 90U, 91U,
            109U, 115U, 117U, 121U, 128U, 134U, 142U, 150U, 152U, 156U, 158U, 171U,
            184U, 185U, 186U, 187U, 188U, 200U, 213U, 221U, 226U, 228U, 238U, 240U,
            258U, 271U, 272U, 273U, 290U, 319U, 329U, 363U
        };

        public static bool HasName(uint type) => Allowed.Contains(type);

        public static bool HasName(int type) => HasName((uint)type);

        public static bool HasName(long type) => HasName((uint)type);
    }
}