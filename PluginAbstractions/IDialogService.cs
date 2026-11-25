namespace PluginAbstractions;

public interface IDialogService
{
    bool Confirm(string title, string message);
}

/// <summary>Set by the host at startup. Plugins and base classes use this instead of referencing UI libraries.</summary>
public static class Dialogs
{
    private sealed class DefaultDialogs : IDialogService
    {
        public bool Confirm(string title, string message) =>
            System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
    }

    public static IDialogService Current { get; set; } = new DefaultDialogs();
}
