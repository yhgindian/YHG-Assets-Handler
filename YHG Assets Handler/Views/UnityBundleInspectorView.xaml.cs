using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using YHG_Assets_Handler.ViewModels;

namespace YHG_Assets_Handler.Views
{
    /// <summary>
    /// Interaction logic for UnityBundleInspectorView.xaml
    /// </summary>
    public partial class UnityBundleInspectorView : UserControl
    {
        public UnityBundleInspectorView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is UnityBundleInspectorViewModel vm)
            {
                vm.SetControls(BundleGrid);
            }
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid dg && DataContext is UnityBundleInspectorViewModel vm)
            {
                vm.UpdateSelection(dg.SelectedItems);
            }
        }
    }
}
