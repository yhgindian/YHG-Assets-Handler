using AssetsHandler.Controls;
using System.Collections.Generic;
using System.Windows;
using AssetsTools.NET.Extra;
using AssetsHandler.Plugin.Unity.ViewModels.Unity;
using AssetsHandler.Plugin.Unity.Workspace.UnityAssets;

namespace AssetsHandler.Plugin.Unity.Views.Windows.Unity
{
    /// <summary>
    /// Interaction logic for AddAssetView.xaml
    /// </summary>
    public partial class AddAssetView : DarkWindow
    {
        public AddAssetView(UnityAssetWorkspace workspace)
        {
            InitializeComponent();

            var vm = new AddAssetViewModel(workspace, workspace.LoadedFiles);
            DataContext = vm;

            // ViewModel ke event ko listen karke window close karna
            vm.RequestClose += success =>
            {
                DialogResult = success;
                Close();
            };
        }
    }
}