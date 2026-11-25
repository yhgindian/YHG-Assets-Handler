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

    /// <summary>
    /// Optional nested submenu path under <see cref="Location"/>, dot- or slash-separated,
    /// e.g. "Bussid" or "Bussid.Export". The host creates any missing submenu levels on the
    /// fly, so plugins never need the root menu (View/Tools) to already contain their group.
    /// Leave null to put the item directly under the root menu.
    /// </summary>
    public string? Path { get; init; }

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