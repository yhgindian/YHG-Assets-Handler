using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows;
using PluginAbstractions;

namespace YHG_Assets_Handler.Plugins;

/// <summary>
/// Host side of the plugin system: discovers plugin folders, loads them into the default
/// AssemblyLoadContext (required so WPF pack URIs resolve) and keeps the registries.
/// The host knows nothing about what a plugin does.
/// </summary>
public sealed class PluginManager
{
    public static PluginManager Instance { get; } = new();

    private readonly List<LoadedPlugin> _plugins = new();
    private readonly Dictionary<FormatKey, (string PluginId, FormatDefinition Def)> _formats = new();
    private readonly List<(string PluginId, MenuContribution Menu)> _menus = new();
    private readonly List<(string PluginId, IExplorerFilter Filter)> _filters = new();
    private readonly List<(string PluginId, ViewRegistration View)> _views = new();
    private readonly Dictionary<string, AssemblyDependencyResolver> _resolvers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private List<FormatDefinition> _detectOrder = new();
    private bool _hooked;

    private sealed record LoadedPlugin(IPlugin Plugin, string Directory);

    public string PluginsRoot { get; } = Path.Combine(AppContext.BaseDirectory, "plugins");

    public IReadOnlyList<PluginLoadResult> Results { get; private set; } = Array.Empty<PluginLoadResult>();
    public IReadOnlyList<(string PluginId, MenuContribution Menu)> Menus => _menus;

    // ───────────── Loading ─────────────

    public void LoadAll()
    {
        var results = new List<PluginLoadResult>();
        HookResolvers();

        // Pending uninstalls/updates are applied now, before any plugin dll gets locked by being loaded.
        PluginStateStore.ApplyPending(PluginsRoot);

        if (Directory.Exists(PluginsRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(PluginsRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                results.Add(LoadFolder(dir));
        }

        Results = results;
        RebuildDetectOrder();
    }

    private PluginLoadResult LoadFolder(string dir)
    {
        string name = Path.GetFileName(dir);
        try
        {
            var manifest = ReadManifest(dir);
            var manifestInfo = manifest == null ? null : new PluginInfo { Id = manifest.Id, Name = manifest.Name, Version = manifest.Version };

            // Disabled plugins are not even loaded (known from plugin.json), so their dll is never touched.
            if (manifest != null && PluginStateStore.IsDisabled(manifest.Id))
                return new PluginLoadResult { Directory = dir, Info = manifestInfo, Status = PluginStatus.Disabled };

            string entry = !string.IsNullOrWhiteSpace(manifest?.EntryAssembly)
                ? manifest!.EntryAssembly
                : name + ".dll";
            string entryPath = Path.Combine(dir, entry);

            if (!File.Exists(entryPath))
                return new PluginLoadResult { Directory = dir, Info = manifestInfo, Status = PluginStatus.Failed, Error = $"Entry assembly not found: {entry}" };

            if (manifest != null && manifest.MinHostApiVersion > PluginApi.Version)
                return new PluginLoadResult { Directory = dir, Info = manifestInfo, Status = PluginStatus.Incompatible,
                    Error = $"Needs plugin API v{manifest.MinHostApiVersion}, host provides v{PluginApi.Version}" };

            _resolvers[dir] = new AssemblyDependencyResolver(entryPath);

            var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(entryPath);
            var type = asm.GetExportedTypes().FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });
            if (type == null)
                return new PluginLoadResult { Directory = dir, Info = manifestInfo, Status = PluginStatus.Failed, Error = "No IPlugin implementation found." };

            var plugin = (IPlugin)Activator.CreateInstance(type)!;
            var info = plugin.Info;

            if (info.MinHostApiVersion > PluginApi.Version)
                return new PluginLoadResult { Directory = dir, Info = info, Status = PluginStatus.Incompatible,
                    Error = $"Needs plugin API v{info.MinHostApiVersion}, host provides v{PluginApi.Version}" };

            if (PluginStateStore.IsDisabled(info.Id))
                return new PluginLoadResult { Directory = dir, Info = info, Status = PluginStatus.Disabled };

            var scope = new Scope(this, info.Id, dir);
            try
            {
                plugin.Initialize(scope);
            }
            catch
            {
                RemoveAllOf(info.Id);   // half-registered plugin must not leak anything
                throw;
            }

            _plugins.Add(new LoadedPlugin(plugin, dir));
            return new PluginLoadResult { Directory = dir, Info = info, Status = PluginStatus.Loaded };
        }
        catch (Exception ex)
        {
            var m = ReadManifestSafe(dir);
            return new PluginLoadResult
            {
                Directory = dir,
                Info = m == null ? null : new PluginInfo { Id = m.Id, Name = m.Name, Version = m.Version },
                Status = PluginStatus.Failed,
                Error = ex.GetBaseException().Message
            };
        }
    }

    private static PluginManifest? ReadManifestSafe(string dir)
    {
        try { return ReadManifest(dir); } catch { return null; }
    }

    public static PluginManifest? ReadManifest(string dir)
    {
        string file = Path.Combine(dir, "plugin.json");
        if (!File.Exists(file)) return null;
        return JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(file));
    }

    private void HookResolvers()
    {
        if (_hooked) return;
        _hooked = true;

        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            foreach (var r in _resolvers.Values)
            {
                string? path = r.ResolveAssemblyToPath(name);
                if (path != null && File.Exists(path))
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            return null;
        };

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, lib) =>
        {
            foreach (var r in _resolvers.Values)
            {
                string? path = r.ResolveUnmanagedDllToPath(lib);
                if (path != null && File.Exists(path))
                    return NativeLibraryLoad(path);
            }
            return IntPtr.Zero;
        };
    }

    private static IntPtr NativeLibraryLoad(string path)
        => System.Runtime.InteropServices.NativeLibrary.TryLoad(path, out var h) ? h : IntPtr.Zero;

    public void ShutdownAll()
    {
        foreach (var p in _plugins)
        {
            try { p.Plugin.Shutdown(); } catch { }
        }
    }

    // ───────────── Registries ─────────────

    private void RemoveAllOf(string pluginId)
    {
        lock (_gate)
        {
            foreach (var k in _formats.Where(kv => kv.Value.PluginId == pluginId).Select(kv => kv.Key).ToList())
                _formats.Remove(k);
            _menus.RemoveAll(m => m.PluginId == pluginId);
            _filters.RemoveAll(f => f.PluginId == pluginId);

            foreach (var v in _views.Where(v => v.PluginId == pluginId).ToList())
            {
                _views.Remove(v);
                if (Application.Current != null)
                    Application.Current.Resources.Remove(new DataTemplateKey(v.View.ViewModelType));
            }
        }
    }

    private void RebuildDetectOrder()
    {
        lock (_gate)
        {
            _detectOrder = _formats.Values
                .Select(v => v.Def)
                .Where(d => d.Detector != null)
                .OrderByDescending(d => d.Priority)
                .ToList();
        }
    }

    public FormatDefinition? GetFormat(FormatKey key)
        => _formats.TryGetValue(key, out var v) ? v.Def : null;

    public bool IsHidden(string filePath)
    {
        foreach (var (_, f) in _filters)
        {
            try { if (f.ShouldHide(filePath)) return true; } catch { }
        }
        return false;
    }

    public ExplorerViewModelFactoryResult CreateViewModel(ExplorerItem item)
    {
        var def = GetFormat(item.Format);
        if (def?.ViewModelFactory == null) return default;
        try { return new ExplorerViewModelFactoryResult(def.ViewModelFactory(item), null); }
        catch (Exception ex) { return new ExplorerViewModelFactoryResult(null, ex.Message); }
    }

    /// <summary>Runs all detectors (priority order) and returns the most confident match.</summary>
    public FormatKey Detect(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return FormatKey.Unknown;

        List<FormatDefinition> order;
        lock (_gate) order = _detectOrder;
        if (order.Count == 0) return FormatKey.Unknown;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var best = DetectionResult.None;

            foreach (var def in order)
            {
                try
                {
                    fs.Position = 0;
                    var r = def.Detector!.Detect(fs, 0);
                    if (r.IsMatch && r.Confidence > best.Confidence)
                    {
                        best = r;
                        if (best.Confidence >= 100) break;
                    }
                }
                catch { /* a broken detector must not break the others */ }
            }
            return best.Format;
        }
        catch
        {
            return FormatKey.Unknown;
        }
    }

    // ───────────── Per-plugin scope ─────────────

    private sealed class Scope : IPluginRegistry
    {
        private readonly PluginManager _m;
        private readonly string _id;
        public string PluginDirectory { get; }

        public Scope(PluginManager m, string id, string dir) { _m = m; _id = id; PluginDirectory = dir; }

        public void RegisterFormat(FormatDefinition format)
        {
            lock (_m._gate)
            {
                if (_m._formats.TryGetValue(format.Key, out var existing) && existing.PluginId != _id)
                    throw new InvalidOperationException($"Format '{format.Key}' is already registered by plugin '{existing.PluginId}'.");
                _m._formats[format.Key] = (_id, format);
            }
        }

        public void RegisterView(Type viewModelType, Type viewType)
        {
            var template = new DataTemplate(viewModelType) { VisualTree = new FrameworkElementFactory(viewType) };
            template.Seal();
            Application.Current.Resources[template.DataTemplateKey] = template;
            lock (_m._gate) _m._views.Add((_id, new ViewRegistration(viewModelType, viewType)));
        }

        public void RegisterMenu(MenuContribution menu)
        {
            lock (_m._gate) _m._menus.Add((_id, menu));
        }

        public void RegisterExplorerFilter(IExplorerFilter filter)
        {
            lock (_m._gate) _m._filters.Add((_id, filter));
        }
    }
}

public readonly record struct ExplorerViewModelFactoryResult(BaseViewModel? ViewModel, string? Error);
