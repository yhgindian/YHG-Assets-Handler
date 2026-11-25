using AssetsHandler.Controls;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsHandler.Plugin.Unity.ViewModels.Unity;
using AssetsHandler.Plugin.Unity.Workspace.UnityAssets;

namespace AssetsHandler.Plugin.Unity.Views.Windows.Unity
{
    public partial class UnityPPtrPickerView : DarkWindow
    {
        public UnityPPtrPickerViewModel ViewModel { get; }

        public UnityPPtrPickerView(
            UnityAssetWorkspace w,
            AssetsFileInstance? reference = null,
            int? defaultFileId = null,
            long? defaultPathId = null,
            params AssetClassID[] allowed)
        {
            InitializeComponent();
            ViewModel = new UnityPPtrPickerViewModel(w, reference, null, defaultFileId, defaultPathId, allowed);
            this.DataContext = ViewModel;

            ViewModel.RequestClose += () =>
            {
                this.DialogResult = true;
                this.Close();
            };
        }
    }
}