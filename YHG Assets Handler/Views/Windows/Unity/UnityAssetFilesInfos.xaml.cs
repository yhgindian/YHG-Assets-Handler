using AssetsHandler.Controls;
using System.Windows;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.ViewModels.Unity;
using YHG_Assets_Handler.Workspace.UnityAssets;
using static YHG_Assets_Handler.ViewModels.Unity.AssetInfoViewModel;

namespace YHG_Assets_Handler.Views.Windows.Unity
{
    public partial class UnityAssetFilesInfos : DarkWindow
    {
        private readonly AssetInfoViewModel _viewModel;
        private string? _originalPlatform;
        private bool _isEditingPlatform;

        public UnityAssetFilesInfos(UnityAssetWorkspace workspace)
        {
            InitializeComponent();
            _viewModel = new AssetInfoViewModel(workspace);
            DataContext = _viewModel;
        }

        private void BtnAddExternal_Click(object sender, RoutedEventArgs e)
        {
            var addDialog = new AddExternalView
            {
                Owner = this
            };

            if (addDialog.ShowDialog() == true && addDialog.ResultExternal != null)
            {
                _viewModel.AddExternal(addDialog.ResultExternal);
            }
        }

        private void BtnEditExternal_Click(object sender, RoutedEventArgs e)
        {
            var selectedExtern = _viewModel.GetSelectedExternal();
            if (selectedExtern == null) return;

            var editDialog = new AddExternalView(selectedExtern)
            {
                Owner = this
            };

            if (editDialog.ShowDialog() == true && editDialog.ResultExternal != null)
            {
                _viewModel.UpdateSelectedExternal(editDialog.ResultExternal);
            }
        }

        private void BtnEditPlateform_Click(object sender, RoutedEventArgs e)
        {
            if (_isEditingPlatform)
                return;

            _originalPlatform = _viewModel.GeneralInfo.Platform;

            _viewModel.LoadPlatformOptions();

            CmbPlatform.ItemsSource = _viewModel.PlatformOptions;
            CmbPlatform.SelectedItem = _viewModel.SelectedPlatform;

            TxtPlatform.Visibility = Visibility.Collapsed;
            CmbPlatform.Visibility = Visibility.Visible;

            BtnEditPlatform.Visibility = Visibility.Collapsed;
            BtnCancelPlatform.Visibility = Visibility.Visible;
            BtnApplyPlatform.Visibility = Visibility.Visible;

            _isEditingPlatform = true;
        }

        private void BtnCancelPlatform_Click(object sender, RoutedEventArgs e)
        {
            CmbPlatform.SelectedItem = _viewModel.PlatformOptions
                .FirstOrDefault(p => p.ToString() == _originalPlatform);

            ExitPlatformEditMode();
        }

        private void BtnApplyPlatform_Click(object sender, RoutedEventArgs e)
        {
            if (CmbPlatform.SelectedItem is not PlatformOption selected)
                return;

            bool result = ConfirmationDialog.Show(
                title: "Change Target Platform",
                message: $"Are you sure you want to change the target platform to '{selected.Name}'?\n\n" +
                         "Changing the platform metadata may cause compatibility issues with certain asset formats, " +
                         "particularly platform-specific textures and shaders.\n\n" +
                         "This operation does not convert assets into the target platform's native formats.\n\n" +
                         "Make sure you have a backup before proceeding.",
                owner: this);

            if (!result)
                return;

            if (_viewModel.UpdatePlatform(selected.Value))
                ExitPlatformEditMode();
        }

        private void ExitPlatformEditMode()
        {
            TxtPlatform.Visibility = Visibility.Visible;
            CmbPlatform.Visibility = Visibility.Collapsed;

            BtnEditPlatform.Visibility = Visibility.Visible;
            BtnCancelPlatform.Visibility = Visibility.Collapsed;
            BtnApplyPlatform.Visibility = Visibility.Collapsed;

            _isEditingPlatform = false;
        }
    }
}