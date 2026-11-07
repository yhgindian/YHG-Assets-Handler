using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AssetsHandler.Core.NotificationManager;
using YHG_Assets_Handler.Editor.Unity;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.Views.Windows.Unity
{
    public partial class UnityAssetDataViewer
    {
        private readonly UnityAssetWorkspace _workspace;
        private readonly AssetContainer _assetContainer;

        public UnityAssetDataViewer(UnityAssetWorkspace workspace, AssetContainer container)
        {
            InitializeComponent();
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _assetContainer = container ?? throw new ArgumentNullException(nameof(container));

            DataTreeView.Init(_workspace);
            TxtStatus.Text = "Loading...";
            Loaded += OnWindowLoaded;
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnWindowLoaded;
            // Pehle window ko render hone do, phir data load karo
            await Dispatcher.Yield(DispatcherPriority.Background);
            await LoadAssetAsync();
        }

        private async Task LoadAssetAsync()
        {
            try
            {
                AssetNameUtils.GetDisplayNameFast(_workspace, _assetContainer, true, out string assetName, out string typeName);
                TxtAssetName.Text = assetName;
                TxtPathId.Text = _assetContainer.PathId.ToString();
                TxtClassName.Text = typeName;
                Title = $"Asset Inspector - {assetName} ({_assetContainer.PathId})";

                await DataTreeView.LoadComponentAsync(_assetContainer);
                TxtStatus.Text = $"Loaded fields for {assetName}";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error: {ex.Message}";
                NotificationManager.Error(ex.Message, "Load Failed");
            }
        }

        private async void BtnEditData_Click(object sender, RoutedEventArgs e)
        {
            await DataEditor.OpenEditorAsync(new List<AssetContainer> { _assetContainer }, _workspace);
        }
    }
}