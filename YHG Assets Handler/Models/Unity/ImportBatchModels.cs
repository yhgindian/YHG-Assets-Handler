using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.Models.Unity
{
    public class ImportBatchInfo
    {
        public AssetContainer Container { get; set; } = null!;
        public string ImportFilePath { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
        public string AssetFile { get; set; } = string.Empty;
        public long PathId { get; set; }
    }

    public partial class ImportBatchItem : ObservableObject
    {
        public ImportBatchInfo Info { get; set; }

        public string Description => Info.AssetName;
        public string AssetFileName => Info.AssetFile;
        public long PathId => Info.PathId;

        [ObservableProperty]
        private ObservableCollection<string> _matchingFiles = [];

        [ObservableProperty]
        private int _selectedIndex = -1;

        public ImportBatchItem(ImportBatchInfo info)
        {
            Info = info;
        }

        public string GetMatchSuffix(string extension)
        {
            return extension != "*"
                ? $"-{AssetFileName}-{PathId}.{extension}"
                : $"-{AssetFileName}-{PathId}";
        }
    }
}