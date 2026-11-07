using System.Windows;
using System.Windows.Controls;
using YHG_Assets_Handler.ViewModels;

namespace YHG_Assets_Handler.Views
{
    public partial class UnityAssetsInspectorView : UserControl
    {
        public UnityAssetsInspectorView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is UnityAssetsInspectorViewModel vm)
            {
                vm.SetControls(AssetGrid);
            }
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid dg && DataContext is UnityAssetsInspectorViewModel vm)
            {
                vm.UpdateSelection(dg.SelectedItems);
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is TabControl tc &&  DataContext is UnityAssetsInspectorViewModel vm)
            {
                vm.UpdateInspectorPanel(tc.SelectedIndex);
            }
        }
    }
}