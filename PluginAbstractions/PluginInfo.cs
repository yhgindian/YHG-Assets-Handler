namespace PluginAbstractions;

/// <summary>Metadata shown in the Plugin Manager / About window.</summary>
public sealed record PluginInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string Author { get; init; } = "";
    public string Credits { get; init; } = "";
    public string License { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>Minimum <see cref="PluginApi.Version"/> this plugin needs.</summary>
    public int MinHostApiVersion { get; init; } = 1;
}
