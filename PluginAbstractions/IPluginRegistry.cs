namespace PluginAbstractions;

/// <summary>
/// Handed to <see cref="IPlugin.Initialize"/>. The host tags everything registered through it with the plugin id,
/// so a plugin can be disabled/uninstalled as a unit.
/// </summary>
public interface IPluginRegistry
{
    void RegisterFormat(FormatDefinition format);
    void RegisterView(Type viewModelType, Type viewType);
    void RegisterMenu(MenuContribution menu);
    void RegisterExplorerFilter(IExplorerFilter filter);

    /// <summary>Directory the plugin was loaded from (for data files, native libs, resources).</summary>
    string PluginDirectory { get; }
}
