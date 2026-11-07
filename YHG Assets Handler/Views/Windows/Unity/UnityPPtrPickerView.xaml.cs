using AssetsHandler.Controls;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using YHG_Assets_Handler.ViewModels.Unity;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.Views.Windows.Unity
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