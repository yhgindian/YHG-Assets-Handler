using AssetsHandler.Controls;
using AssetsHandler.Plugin.Unity.ViewModels.Unity;

namespace AssetsHandler.Plugin.Unity.Views.Windows.Unity
{
    public partial class BatchImportView : DarkWindow
    {
        public BatchImportView()
        {
            InitializeComponent();
        }

        public BatchImportView(BatchImportViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.RequestClose = success =>
            {
                DialogResult = success;
                Close();
            };
        }
    }
}