using System.Text.Json.Serialization;

namespace YHG_Assets_Handler.Plugins;

/// <summary>plugin.json that sits in every plugin folder.</summary>
public sealed class PluginManifest
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("entryAssembly")] public string EntryAssembly { get; set; } = "";
    [JsonPropertyName("minHostApiVersion")] public int MinHostApiVersion { get; set; } = 1;
}
