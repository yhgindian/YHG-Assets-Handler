using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using YHG_Assets_Handler.Utils;
using YHG_Assets_Handler.Utils.Unity;

namespace YHG_Assets_Handler.Workspace.UnityAssets
{
    public class UnityAssetWorkspace : BaseWorkspace
    {
        public AssetsManager am;
        public bool fromBundle;

        // Retain manager and file handles on dispose (externally owned by bundle container)
        private readonly bool _ownsManager;

        public event Action<AssetContainer, bool>? AssetUpdated;
        public event Action<AssetContainer>? AssetRemoved;
        public event Func<AssetPPtr, bool>? CheckAssetExistRequested;
        public event Action<AssetPPtr>? GoToAssetRequested;
        public event Action<AssetsFileInstance>? FileLoaded;
        public event Action<bool>? MarkModified;

        public List<AssetsFileInstance> LoadedFiles { get; } = [];
        public HashSet<string> LoadedFileNames { get; } = [];
        public string? UnityVersion;
        public Dictionary<AssetPPtr, AssetContainer> LoadedAssets { get; } = [];

        public Dictionary<string, AssetsFileInstance> LoadedFileLookup { get; } = [];
        public HashSet<AssetPPtr> RemovedAssets { get; } = [];
        public HashSet<AssetPPtr> NewAssets { get; } = [];

        public HashSet<AssetsFileInstance> ChangedFiles = [];

        // MonoBehaviour
        private bool _setMonoTempGeneratorsYet;

        private static readonly Regex SplitFileRegex = new(@"\.split\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        public static bool IsSplitFile(string path) => SplitFileRegex.IsMatch(path);

        public UnityAssetWorkspace(bool fromBundle = false) : this(AssetManagerUtils.Init(), fromBundle, true)
        {
        }

        public UnityAssetWorkspace(AssetsManager am, bool fromBundle, bool ownsManager = true)
        {
            this.am = am;
            this.fromBundle = fromBundle;
            _ownsManager = ownsManager;
        }

        public void LoadAssetsFile(AssetsFileInstance fromFile, bool loadDependencies)
        {
            string fromFilePath = fromFile.path.ToLower();
            if (LoadedFileNames.Contains(fromFilePath))
                return;

            var uv = fromFile.file.Metadata.UnityVersion;
            UnityVersion ??= uv;
            if (am.ClassDatabase == null && !string.IsNullOrEmpty(uv) && uv != "0.0.0")
                am.LoadClassDatabaseFromPackage(uv);

            LoadedFiles.Add(fromFile);
            LoadedFileNames.Add(fromFilePath);

            var customAssetInfos = new List<AssetFileInfo>(fromFile.file.AssetInfos.Count);

            foreach (AssetFileInfo info in fromFile.file.AssetInfos)
            {
                AssetContainer cont = new AssetContainer(info, fromFile);

                LoadedAssets[cont.AssetPPtr] = cont;
                customAssetInfos.Add(cont);
            }

            fromFile.file.Metadata.AssetInfos = customAssetInfos;
            fromFile.file.GenerateQuickLookup();

            if (loadDependencies)
            {
                for (int i = 0; i < fromFile.file.Metadata.Externals.Count; i++)
                {
                    AssetsFileInstance dep = fromFile.GetDependency(am, i);
                    if (dep == null)
                        continue;

                    string depPath = dep.path.ToLower();
                    if (!LoadedFileNames.Contains(depPath))
                        LoadAssetsFile(dep, true);
                }
            }
        }

        public AssetsFileInstance? LoadAssetsFile(string filePath, bool loadDependencies = false,
            Action<string>? statusCallback = null, bool raiseFileLoaded = true)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            if (IsSplitFile(filePath))
            {
                Status.Write($"[Load] Skipped split file: {Path.GetFileName(filePath)}", StatusType.Info);
                return null;
            }

            string normalizedPath = filePath.ToLowerInvariant();
            if (LoadedFileNames.Contains(normalizedPath))
                return LoadedFileLookup.TryGetValue(normalizedPath, out var existing) ? existing : null;

            statusCallback?.Invoke($"Importing {Path.GetFileName(filePath)}...");
            if (FileTypeDetector.DetectFileType(filePath) != AssetsHandler.Core.FileType.UnityAsset) return null;

            AssetsFileInstance? inst = null;
            try
            {
                inst = am.LoadAssetsFile(filePath, loadDeps: false);
                if (inst?.file == null)
                {
                    if (inst != null) { try { am.UnloadAssetsFile(inst); } catch { } }
                    return null;
                }
            }
            catch (Exception ex)
            {
                Status.Write($"[Load] Failed to read {Path.GetFileName(filePath)}: {ex.Message}", StatusType.Error);
                if (inst != null) { try { am.UnloadAssetsFile(inst); } catch { } }
                return null;
            }

            var uv = inst.file.Metadata.UnityVersion;
            UnityVersion ??= uv;

            if (UnityVersion != uv)
            {
                Status.Write($"This is different version assets file! (Expected: {UnityVersion}, File: {uv})", StatusType.Warning);
                try { am.UnloadAssetsFile(inst); } catch { }
                return null;
            }

            if (am.ClassDatabase == null)
            {
                statusCallback?.Invoke($"Loading Cldb ({uv})...");
                am.LoadClassDatabaseFromPackage(uv);
            }

            statusCallback?.Invoke("Indexing workspace assets...");
            LoadAssetsFile(inst, loadDependencies);

            if (raiseFileLoaded)
                FileLoaded?.Invoke(inst);

            return inst;
        }
        public List<AssetsFileInstance> LoadAssetsFiles(IEnumerable<string> filePaths, Action<string>? statusCallback = null)
        {
            var loadedList = new List<AssetsFileInstance>();
            foreach (var path in filePaths)
            {
                var inst = LoadAssetsFile(path, false, statusCallback);
                if (inst != null)
                {
                    loadedList.Add(inst);
                }
            }
            return loadedList;
        }

        public AssetFileInfo? GetAssetFileInfo(AssetsFileInstance fileInst, AssetTypeValueField pptrField)
        {
            return GetAssetFileInfo(fileInst, pptrField["m_FileID"].AsInt, pptrField["m_PathID"].AsLong);
        }

        public AssetFileInfo? GetAssetFileInfo(AssetsFileInstance fileInst, int fileId, long pathId)
        {
            if (fileId != 0)
            {
                fileInst = fileInst.GetDependency(am, fileId - 1);
            }
            if (fileInst == null)
            {
                return null;
            }

            return fileInst.file.GetAssetInfo(pathId);
        }

        public AssetContainer? GetAssetContainer(AssetsFileInstance fileInst, AssetTypeValueField pptrField)
        {
            return GetAssetContainer(fileInst, pptrField["m_FileID"].AsInt, pptrField["m_PathID"].AsLong);
        }

        public AssetContainer? GetAssetContainer(AssetsFileInstance? fileInst, int fileId, long pathId, bool forceLoadDep = false)
        {
            if (fileInst == null)
                return null;

            if (fileId != 0)
            {
                fileInst = forceLoadDep
                    ? AssetContext.GetDepLoad(this, fileInst, fileId)
                    : fileInst.GetDependency(am, fileId - 1);
                if (fileInst == null)
                    return null;
            }

            var pptr = new AssetPPtr(fileInst.path, pathId);
            if (!LoadedAssets.TryGetValue(pptr, out AssetContainer? cont) || cont == null)
                return null;

            bool isMonoBehaviour = cont.TypeId == (int)AssetClassID.MonoBehaviour || cont.TypeId < 0;

            if (isMonoBehaviour && !_setMonoTempGeneratorsYet && !cont.FileInstance.file.Metadata.TypeTreeEnabled)
            {
                string dataDir = PathUtils.GetAssetsFileDirectory(fileInst);
                SetMonoTempGenerators(dataDir);
            }

            if (cont.HasValueField)
                return cont;

            try
            {
                AssetTypeTemplateField? tempField = GetTemplateField(cont);
                if (tempField == null)
                {
                    Status.Write($"[Workspace] Missing template field for PathId: {pathId}", StatusType.Warning);
                    return cont;
                }

                RefTypeManager? refMan = isMonoBehaviour ? am.GetRefTypeManager(fileInst) : null;

                AssetTypeValueField baseField = tempField.MakeValue(cont.FileReader, cont.AbsoluteByteStart, refMan);
                cont.BaseValueField = baseField;

                if (isMonoBehaviour)
                {
                    cont.HasProperMonoFields = _setMonoTempGeneratorsYet;
                }

                return cont;
            }
            catch (Exception ex)
            {
                cont.BaseValueField = null;
                if (isMonoBehaviour)
                    cont.HasProperMonoFields = false;

                Status.Write($"[Workspace] Failed to decode fields for PathId: {pathId}. Error: {ex.Message}", StatusType.Error);
                return null;
            }
        }

        public AssetTypeValueField? GetBaseField(AssetContainer? cont)
        {
            if (cont == null)
                return null;

            if (cont.HasValueField)
                return cont.BaseValueField;

            cont = GetAssetContainer(cont.FileInstance, 0, cont.PathId, false);
            return cont?.BaseValueField;
        }

        public AssetTypeValueField? GetBaseField(AssetsFileInstance fileInst, int fileId, long pathId)
        {
            AssetContainer? cont = GetAssetContainer(fileInst, fileId, pathId, false);
            if (cont != null)
                return GetBaseField(cont);
            else
                return null;
        }

        public AssetTypeValueField? GetBaseField(AssetsFileInstance fileInst, AssetTypeValueField pptrField)
        {
            int fileId = pptrField["m_FileID"].AsInt;
            long pathId = pptrField["m_PathID"].AsLong;

            AssetContainer? cont = GetAssetContainer(fileInst, fileId, pathId, false);
            if (cont != null)
                return GetBaseField(cont);
            else
                return null;
        }

        public AssetTypeTemplateField GetTemplateField(AssetContainer cont, bool forceCldb = false, bool skipMonoBehaviourFields = false)
        {
            AssetReadFlags readFlags = AssetReadFlags.None;
            if (skipMonoBehaviourFields && cont.TypeId == (int)AssetClassID.MonoBehaviour)
            {
                readFlags |= AssetReadFlags.SkipMonoBehaviourFields | AssetReadFlags.ForceFromCldb;
            }

            return am.GetTemplateBaseField(cont.FileInstance, cont, readFlags);
        }

        public void GenerateAssetsFileLookup()
        {
            foreach (AssetsFileInstance inst in LoadedFiles)
            {
                LoadedFileLookup[inst.path.ToLower()] = inst;
            }
        }

        protected override void OnDispose()
        {
            try
            {
                AssetUpdated = null;
                AssetRemoved = null;

                if (_ownsManager)
                {
                    foreach (var file in LoadedFiles)
                        file.file?.Close();
                }

                LoadedFiles.Clear();
                LoadedFileNames.Clear();
                LoadedAssets.Clear();
                LoadedFileLookup.Clear();
                RemovedAssets.Clear();
                NewAssets.Clear();

                if (_ownsManager)
                    am?.UnloadAll();
            }
            catch
            {
            }
        }

        public void AddOrUpdateAsset(AssetContainer cont, byte[] bytes, bool isNew = false)
        {
            var pptr = cont.AssetPPtr;
            cont.UpdateData(bytes);

            if (isNew)
            {
                cont.FileInstance.file.Metadata.AddAssetInfo(cont);

                LoadedAssets[pptr] = cont;
                NewAssets.Add(pptr);
            }

            MarkChanges(cont.FileInstance);
            AssetUpdated?.Invoke(cont, isNew);
        }

        public void RemoveAsset(AssetContainer cont)
        {
            RemoveAsset(cont, cont.FileInstance);
        }

        public void RemoveAsset(AssetContainer cont, AssetsFileInstance inst)
        {
            var pptr = cont.AssetPPtr;

            LoadedAssets.Remove(pptr);
            inst.file.Metadata.RemoveAssetInfo(cont);

            RemovedAssets.Add(pptr);
            NewAssets.Remove(pptr);

            MarkChanges(inst);
            AssetRemoved?.Invoke(cont);
        }

        public void MarkChanges(AssetsFileInstance inst)
        {
            SetDirty();
            ChangedFiles.Add(inst);
            MarkModified?.Invoke(base.IsDirty);
        }

        public bool SetMonoTempGenerators(string fileDir)
        {
            if (_setMonoTempGeneratorsYet)
            {
                Status.Write("[Mono] Generator already configured. Skipping set request.");
                return true;
            }

            Status.Write($"[Mono] Setting Mono generator. Base search directory: {fileDir}");

            try
            {
                static bool HasDlls(string path)
                {
                    try
                    {
                        return Directory.Exists(path) && Directory.EnumerateFiles(path, "*.dll").Any();
                    }
                    catch
                    {
                        return false;
                    }
                }

                string? resolvedManagedDir = null;

                if (!string.IsNullOrWhiteSpace(fileDir) && Directory.Exists(fileDir))
                {
                    if (HasDlls(fileDir))
                    {
                        Status.Write($"[Mono] Detected DLLs directly in target directory: {fileDir}");
                        resolvedManagedDir = fileDir;
                    }
                    else
                    {
                        string subManaged = Path.Combine(fileDir, "Managed");
                        if (HasDlls(subManaged))
                        {
                            Status.Write($"[Mono] Detected Managed subfolder: {subManaged}");
                            resolvedManagedDir = subManaged;
                        }
                        else
                        {
                            try
                            {
                                var dataDir = Directory.EnumerateDirectories(fileDir, "*_Data").FirstOrDefault();
                                if (dataDir != null)
                                {
                                    string dataManaged = Path.Combine(dataDir, "Managed");
                                    if (HasDlls(dataManaged))
                                    {
                                        Status.Write($"[Mono] Detected Unity *_Data Managed folder: {dataManaged}");
                                        resolvedManagedDir = dataManaged;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Status.Write($"[Mono] Error scanning for *_Data directory: {ex.Message}", StatusType.Warning);
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(resolvedManagedDir))
                {
                    Status.Write($"[Mono] Initializing MonoCecilTempGenerator at: {resolvedManagedDir}");
                    am.MonoTempGenerator = new MonoCecilTempGenerator(resolvedManagedDir);
                    _setMonoTempGeneratorsYet = true;
                    am.ClearMonoTemplateCache();
                    Status.Write($"[Mono] Auto-detected Managed folder successfully set: {resolvedManagedDir}", StatusType.Success);
                    return true;
                }

                Status.Write("[Mono] Managed folder could not be auto-detected. Requesting user selection...", StatusType.Warning);

                NotificationManager.Warning(
                    "Mono Managed folder not found automatically. Please locate the Managed folder containing assemblies.",
                    "Assemblies Required"
                );

                var ofd = new OpenFolderDialog
                {
                    Title = "Select Managed Folder Containing Assembly DLLs"
                };

                if (!string.IsNullOrWhiteSpace(fileDir) && Directory.Exists(fileDir))
                    ofd.InitialDirectory = fileDir;

                string? selectedFolder = null;
                if (ofd.ShowDialog() == true)
                {
                    selectedFolder = ofd.FolderName;
                    Status.Write($"[Mono] User selected folder: {selectedFolder}");
                }

                // User cancelled
                if (string.IsNullOrWhiteSpace(selectedFolder))
                {
                    Status.Write("[Mono] Assembly folder selection was cancelled by user.", StatusType.Warning);
                    return false;
                }

                if (HasDlls(selectedFolder))
                {
                    Status.Write($"[Mono] Initializing MonoCecilTempGenerator with user folder: {selectedFolder}");
                    am.MonoTempGenerator = new MonoCecilTempGenerator(selectedFolder);
                    _setMonoTempGeneratorsYet = true;
                    am.ClearMonoTemplateCache();
                    Status.Write($"[Mono] Managed folder set: {selectedFolder}", StatusType.Success);
                    return true;
                }

                NotificationManager.Error(
                    "Selected folder does not contain any .dll assemblies. Mono type generation aborted.",
                    "Assemblies Missing"
                );
                Status.Write($"[Mono] No .dll files found in selected directory: {selectedFolder}", StatusType.Error);
                return false;
            }
            catch (Exception ex)
            {
                Status.Write($"[Mono] Failed to initialize Mono temp generator: {ex.Message}", StatusType.Error);
                return false;
            }
        }

        public AssetContainer? CreateAsset(AssetsFileInstance fileInst, int typeId, long pathId, ushort scriptIndex = 0xffff, AssetTypeValueField? baseField = null, bool addIntoAssets = true)
        {
            string filePath = !string.IsNullOrEmpty(fileInst.path) ? fileInst.path : fileInst.name;
            var pptr = new AssetPPtr(filePath, pathId);

            baseField ??= am.CreateEmptyBaseField(typeId);
            bool isNew = false;

            if (!LoadedAssets.TryGetValue(pptr, out AssetContainer? cont))
            {
                AssetFileInfo? info = AssetCreationHelper.CreateAssetFileInfo(
                    fileInst.file,
                    am.ClassDatabase,
                    pathId,
                    typeId,
                    scriptIndex);

                if (info == null)
                    return null;

                cont = new AssetContainer(info, fileInst, baseField);
                isNew = true;
            }

            if (addIntoAssets && cont != null)
            {
                AddOrUpdateAsset(cont, baseField.WriteToByteArray(), isNew);
            }

            return cont;
        }

        public bool IsAssetExist(AssetPPtr target)
        {
            return CheckAssetExistRequested?.Invoke(target) ?? false;
        }

        public void GoToAsset(AssetPPtr pptr)
        {
            GoToAssetRequested?.Invoke(pptr);
        }

        public void GoToAsset(int fileId, long pathId)
        {
            GoToAsset(new AssetPPtr(fileId, pathId));
        }

        public List<AssetContainer> GetAssetContainersFrom(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return new List<AssetContainer>();

            var temp = new List<AssetContainer>();

            foreach (var kvp in LoadedAssets)
            {
                var cont = kvp.Value;
                if (cont?.FileInstance?.path == null) continue;

                if (string.Equals(cont.FileInstance.path, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    temp.Add(cont);
                }
            }

            return temp;
        }

        public List<AssetContainer> GetAssetContainersFrom(AssetContainer container)
        {
            return this.GetAssetContainersFrom(container.FileInstance.path);
        }

        public List<AssetContainer> GetAssetContainersFrom(AssetsFileInstance instance)
        {
            var infos = instance.file.AssetInfos;
            var list = new List<AssetContainer>(infos.Count);
            foreach (var info in infos)
                if (info is AssetContainer c) list.Add(c);
            return list;
        }
    }
}