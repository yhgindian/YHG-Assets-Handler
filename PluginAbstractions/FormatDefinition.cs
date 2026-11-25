namespace PluginAbstractions;

/// <summary>Describes one file format a plugin understands.</summary>
public sealed class FormatDefinition
{
    /// <summary>Namespaced key, e.g. "vendor.format".</summary>
    public required FormatKey Key { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Optional. Without a detector the format can only be assigned programmatically.</summary>
    public IFileTypeDetector? Detector { get; init; }

    /// <summary>Higher runs first.</summary>
    public int Priority { get; init; } = 0;

    /// <summary>Creates the inspector view model for an explorer item of this format. Null = default viewer.</summary>
    public Func<ExplorerItem, BaseViewModel>? ViewModelFactory { get; init; }

    /// <summary>MahApps PackIconMaterialKind name, e.g. "Cube".</summary>
    public string IconKind { get; init; } = "File";

    /// <summary>Hex colour of the explorer icon, e.g. "#00E6FF".</summary>
    public string IconColor { get; init; } = "#569CD6";
}
