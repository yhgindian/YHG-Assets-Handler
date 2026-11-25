namespace PluginAbstractions;

/// <summary>
/// Entry point of a plugin. Every plugin assembly must contain exactly one public,
/// parameterless-constructible class implementing this interface.
/// </summary>
public interface IPlugin
{
    PluginInfo Info { get; }

    /// <summary>Called once at startup (on the UI thread). Register everything through <paramref name="registry"/>.</summary>
    void Initialize(IPluginRegistry registry);

    /// <summary>Called when the app is closing.</summary>
    void Shutdown() { }
}
