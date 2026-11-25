namespace YHG_Assets_Handler.Plugins;

public enum PluginStatus
{
    Loaded,
    Disabled,
    Failed,
    Incompatible
}

public sealed class PluginLoadResult
{
    public required string Directory { get; init; }
    public PluginAbstractions.PluginInfo? Info { get; init; }
    public PluginStatus Status { get; init; }
    public string? Error { get; init; }
}
