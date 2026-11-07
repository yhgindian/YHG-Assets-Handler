using System.Windows;
using AssetsHandler.Controls;
using AssetsHandler.Core.Filtering;

namespace YHG_Assets_Handler.Views.Windows
{
    public partial class FilterWindow : DarkWindow
    {
        public FilterEditorViewModel ViewModel { get; }

        public FilterWindow(FilterEditorViewModel vm)
        {
            InitializeComponent();
            ViewModel = vm;
            DataContext = vm;

            vm.CloseRequested += result =>
            {
                try { DialogResult = result; }          // ShowDialog() ho to
                catch (InvalidOperationException) { Close(); }   // Show() ho to
            };
        }
    }
}