using AssetsHandler.Controls;
using YHG_Assets_Handler.ViewModels;

namespace YHG_Assets_Handler.Views.Windows
{
    public partial class PluginManagerWindow : DarkWindow
    {
        public PluginManagerWindow()
        {
            InitializeComponent();
            DataContext = new PluginManagerViewModel();
        }
    }
}
