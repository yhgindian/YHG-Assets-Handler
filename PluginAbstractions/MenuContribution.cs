namespace PluginAbstractions;

public enum MenuLocation
{
    View,
    Tools
}

public sealed class MenuContext
{
    public ExplorerItem? CurrentItem { get; init; }
    public BaseViewModel? CurrentViewModel => CurrentItem?.CachedViewModel;
    public System.Windows.Window? Owner { get; init; }

    public T? GetViewModel<T>() where T : class => CurrentViewModel as T;
}

public sealed class MenuContribution
{
    public required MenuLocation Location { get; init; }
    public required string Header { get; init; }

    /// <summary>Menu entries are sorted by Order, then registration order.</summary>
    public int Order { get; init; } = 0;
    public string IconKind { get; init; } = "None";
    public string? InputGestureText { get; init; }

    /// <summary>e.g. "Ctrl+Shift+A". Parsed by the host and registered as a window key binding.</summary>
    public string? Gesture { get; init; }

    /// <summary>Visible only while an item of one of these formats is selected. Null/empty = always visible.</summary>
    public IReadOnlyList<FormatKey>? VisibleForFormats { get; init; }

    public bool SeparatorBefore { get; init; }

    public Func<MenuContext, bool>? CanExecute { get; init; }
    public Action<MenuContext>? Execute { get; init; }
    public Func<MenuContext, Task>? ExecuteAsync { get; init; }
}
