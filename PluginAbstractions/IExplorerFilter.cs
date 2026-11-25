namespace PluginAbstractions;

/// <summary>Lets a plugin hide files while the host enumerates a folder (e.g. *.split0 parts).</summary>
public interface IExplorerFilter
{
    bool ShouldHide(string filePath);
}
