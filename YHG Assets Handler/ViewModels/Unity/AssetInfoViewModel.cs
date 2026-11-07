using AssetsTools.NET;
using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using YHG_Assets_Handler.Models.Unity;
using YHG_Assets_Handler.Models.Unity.AssetInfo;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.ViewModels.Unity
{
    public partial class AssetInfoViewModel : ObservableObject
    {
        private readonly UnityAssetWorkspace _workspace;
        private readonly AssetsManager _am;

        [ObservableProperty]
        private ObservableCollection<AssetsFileDisplayItem> _files = new();

        [ObservableProperty]
        private AssetsFileDisplayItem? _selectedFile;

        // General Info
        [ObservableProperty]
        private GeneralInfo _generalInfo = GeneralInfo.Empty;

        public sealed record PlatformOption(string Name, uint Value)
        {
            public override string ToString() => $"{Name} ({Value})";
        }

        [ObservableProperty]
        private ObservableCollection<PlatformOption> _platformOptions = new();

        [ObservableProperty]
        private PlatformOption? _selectedPlatform;

        // Externals
        [ObservableProperty]
        private ObservableCollection<string> _externalsDisplay = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(MoveUpExternalCommand))]
        [NotifyCanExecuteChangedFor(nameof(MoveDownExternalCommand))]
        [NotifyCanExecuteChangedFor(nameof(RemoveExternalCommand))]
        private int _selectedExternalIndex = -1;
        public bool IsExternalSelected => SelectedExternalIndex >= 0;

        // Script Infos
        [ObservableProperty]
        private ObservableCollection<string> _scriptsDisplay = new();


        public AssetInfoViewModel(UnityAssetWorkspace workspace)
        {
            _workspace = workspace;
            _am = workspace.am;

            var loadedFiles = _am.Files;
            if (loadedFiles != null && loadedFiles.Count > 0)
            {
                foreach (var f in loadedFiles)
                    Files.Add(new(f));

                SelectedFile = Files[0];
            }
        }

        partial void OnSelectedFileChanged(AssetsFileDisplayItem? value)
        {
            var fileInst = value?.Instance;
            if (fileInst?.file == null)
            {
                GeneralInfo = GeneralInfo.Empty;
                ExternalsDisplay.Clear();
                ScriptsDisplay.Clear();
                return;
            }

            // 1. Header & Platform Info
            GeneralInfo = new GeneralInfo(fileInst);

            // 2. Externals Dependencies
            RefreshExternals(fileInst);

            // 3. Scripts
            RefreshScripts(fileInst);
        }

        public void LoadPlatformOptions()
        {
            PlatformOptions.Clear();

            foreach (BuildTarget platform in Enum.GetValues(typeof(BuildTarget)))
            {
                uint value = (uint)platform;

                if (PlatformOptions.Any(x => x.Value == value))
                    continue;

                PlatformOptions.Add(new PlatformOption(
                    platform.ToString(),
                    value));
            }

            var currentValue = SelectedFile?.Instance?.file?.Metadata?.TargetPlatform;

            SelectedPlatform = PlatformOptions.FirstOrDefault(
                x => x.Value == currentValue);
        }

        public bool UpdatePlatform(uint platform)
        {
            var fileInst = SelectedFile?.Instance;

            if (fileInst?.file?.Metadata == null)
                return false;

            fileInst.file.Metadata.TargetPlatform = platform;

            _workspace.MarkChanges(fileInst);

            GeneralInfo = new GeneralInfo(fileInst);

            return true;
        }

        private void RefreshExternals(AssetsFileInstance fileInst)
        {
            ExternalsDisplay.Clear();
            var externals = fileInst.file?.Metadata?.Externals;
            if (externals == null) return;

            for (int i = 0; i < externals.Count; i++)
            {
                var ext = externals[i];
                string target = !string.IsNullOrEmpty(ext.PathName) ? ext.PathName : ext.Guid.ToString();
                ExternalsDisplay.Add($"[{i}] {target}");
            }
        }

        private void RefreshScripts(AssetsFileInstance fileInst)
        {
            ScriptsDisplay.Clear();
            var scripts = fileInst.file.Metadata.ScriptTypes;
            if (scripts == null) return;

            for (int i = 0; i < scripts.Count; i++)
            {
                var pptr = scripts[i];
                var scriptBf = _workspace.GetBaseField(fileInst, pptr.FileId, pptr.PathId);
                string fullName;

                if (scriptBf != null)
                {
                    string ns = scriptBf["m_Namespace"].AsString;
                    string cl = scriptBf["m_ClassName"].AsString;
                    fullName = string.IsNullOrEmpty(ns) ? cl : $"{ns}.{cl}";
                }
                else if (pptr.FileId == 0)
                {
                    fullName = $"{fileInst.name}/{pptr.PathId}";
                }
                else if (fileInst.file.Metadata.Externals != null && pptr.FileId - 1 < fileInst.file.Metadata.Externals.Count)
                {
                    string extName = fileInst.file.Metadata.Externals[pptr.FileId - 1].PathName;
                    fullName = $"{Path.GetFileName(extName)}/{pptr.PathId}";
                }
                else
                {
                    fullName = $"FileID {pptr.FileId} / {pptr.PathId}";
                }

                ScriptsDisplay.Add($"[{i}] {fullName}");
            }
        }

        // External Actions
        private bool CanModifyExternal() => SelectedExternalIndex >= 0 && SelectedFile?.Instance?.file?.Metadata?.Externals != null;
        private bool CanMoveUp() => SelectedExternalIndex > 0;
        private bool CanMoveDown() => SelectedExternalIndex >= 0 && SelectedExternalIndex < ExternalsDisplay.Count - 1;

        [RelayCommand(CanExecute = nameof(CanModifyExternal))]
        private void RemoveExternal()
        {
            var fileInst = SelectedFile?.Instance;
            if (fileInst?.file?.Metadata?.Externals == null || SelectedExternalIndex < 0) return;

            fileInst.file.Metadata.Externals.RemoveAt(SelectedExternalIndex);
            _workspace.MarkChanges(fileInst);
            RefreshExternals(fileInst);
            SelectedExternalIndex = -1;
        }

        [RelayCommand(CanExecute = nameof(CanMoveUp))]
        private void MoveUpExternal()
        {
            var fileInst = SelectedFile?.Instance;
            if (fileInst?.file?.Metadata?.Externals == null || SelectedExternalIndex <= 0) return;

            var list = fileInst.file.Metadata.Externals;
            int idx = SelectedExternalIndex;

            (list[idx], list[idx - 1]) = (list[idx - 1], list[idx]);
            _workspace.MarkChanges(fileInst);
            RefreshExternals(fileInst);
            SelectedExternalIndex = idx - 1;
        }


        [RelayCommand(CanExecute = nameof(CanMoveDown))]
        private void MoveDownExternal()
        {
            var fileInst = SelectedFile?.Instance;
            if (fileInst?.file?.Metadata?.Externals == null || SelectedExternalIndex < 0 || SelectedExternalIndex >= ExternalsDisplay.Count - 1) return;

            var list = fileInst.file.Metadata.Externals;
            int idx = SelectedExternalIndex;

            (list[idx], list[idx + 1]) = (list[idx + 1], list[idx]);
            _workspace.MarkChanges(fileInst);
            RefreshExternals(fileInst);
            SelectedExternalIndex = idx + 1;
        }

        partial void OnSelectedExternalIndexChanged(int value)
        {
            OnPropertyChanged(nameof(IsExternalSelected));
        }

        public AssetsFileExternal? GetSelectedExternal()
        {
            var externals = SelectedFile?.Instance?.file?.Metadata?.Externals;
            if (externals != null && SelectedExternalIndex >= 0 && SelectedExternalIndex < externals.Count)
            {
                return externals[SelectedExternalIndex];
            }
            return null;
        }

        public void AddExternal(AssetsFileExternal newExternal)
        {
            var fileInst = SelectedFile?.Instance;
            if (fileInst?.file?.Metadata?.Externals == null) return;

            fileInst.file.Metadata.Externals.Add(newExternal);
            _workspace.MarkChanges(fileInst);
            RefreshExternals(fileInst);
            SelectedExternalIndex = fileInst.file.Metadata.Externals.Count - 1;
        }

        public void UpdateSelectedExternal(AssetsFileExternal updatedExternal)
        {
            var fileInst = SelectedFile?.Instance;
            if (fileInst?.file?.Metadata?.Externals == null || SelectedExternalIndex < 0) return;

            int currentIdx = SelectedExternalIndex;
            fileInst.file.Metadata.Externals[currentIdx] = updatedExternal;
            _workspace.MarkChanges(fileInst);
            RefreshExternals(fileInst);
            SelectedExternalIndex = currentIdx;
        }
    }
}