using YHG_Assets_Handler.Models;

namespace YHG_Assets_Handler.ViewModels
{
    public class DefaultInspectorViewModel : BaseViewModel
    {
        public ExplorerItem SelectedItem { get; }

        public DefaultInspectorViewModel(ExplorerItem item) : base(item)
        {
            SelectedItem = item;
        }
    }
}