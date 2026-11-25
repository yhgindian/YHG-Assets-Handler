using AssetsHandler.Controls;
using PluginAbstractions;

namespace YHG_Assets_Handler.Models.Host
{
    public sealed class DialogService : IDialogService
    {
        public bool Confirm(string title, string message) => ConfirmationDialog.Show(title, message);
    }
}
