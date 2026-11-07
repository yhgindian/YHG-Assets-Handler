using AssetsHandler.Controls;
using YHG_Assets_Handler.ViewModels.Unity;

namespace YHG_Assets_Handler.Views.Windows.Unity
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