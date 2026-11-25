using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YHG_Assets_Handler.Plugins;

public sealed class PendingInstall
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("stagingDir")] public string StagingDir { get; set; } = "";
    [JsonPropertyName("targetDir")] public string TargetDir { get; set; } = "";
}

/// <summary>Contents of plugins.json (next to settings). Everything here is applied at the next startup.</summary>
public sealed class PluginState
{
    [JsonPropertyName("disabled")] public List<string> Disabled { get; set; } = new();
    [JsonPropertyName("pendingRemoval")] public List<string> PendingRemoval { get; set; } = new();
    [JsonPropertyName("pendingInstalls")] public List<PendingInstall> PendingInstalls { get; set; } = new();
}

/// <summary>Persists plugin enable/disable/uninstall/update intentions. Plugins cannot be unloaded at runtime, so all of it applies on restart.</summary>
public static class PluginStateStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static PluginState? _state;

    public static string FilePath => Path.Combine(AppPathResolver.AppLocalDataDir, "plugins.json");

    public static PluginState Current
    {
        get
        {
            lock (Gate) return _state ??= Load();
        }
    }

    private static PluginState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<PluginState>(File.ReadAllText(FilePath)) ?? new PluginState();
        }
        catch { }
        return new PluginState();
    }

    public static void Save()
    {
        lock (Gate)
        {
            try
            {
                AppPathResolver.EnsureDirectory(AppPathResolver.AppLocalDataDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Json));
            }
            catch { }
        }
    }

    public static bool IsDisabled(string id)
        => Current.Disabled.Any(d => string.Equals(d, id, StringComparison.OrdinalIgnoreCase));

    public static void SetDisabled(string id, bool disabled)
    {
        lock (Gate)
        {
            Current.Disabled.RemoveAll(d => string.Equals(d, id, StringComparison.OrdinalIgnoreCase));
            if (disabled) Current.Disabled.Add(id);
        }
        Save();
    }

    public static bool IsPendingRemoval(string dirName)
        => Current.PendingRemoval.Any(d => string.Equals(d, dirName, StringComparison.OrdinalIgnoreCase));

    public static void SetPendingRemoval(string dirName, bool pending)
    {
        lock (Gate)
        {
            Current.PendingRemoval.RemoveAll(d => string.Equals(d, dirName, StringComparison.OrdinalIgnoreCase));
            if (pending) Current.PendingRemoval.Add(dirName);
        }
        Save();
    }

    public static PendingInstall? FindPendingInstall(string targetDir)
        => Current.PendingInstalls.FirstOrDefault(p => string.Equals(p.TargetDir, targetDir, StringComparison.OrdinalIgnoreCase));

    public static void QueueInstall(PendingInstall install)
    {
        lock (Gate)
        {
            foreach (var old in Current.PendingInstalls.Where(p => string.Equals(p.TargetDir, install.TargetDir, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                TryDelete(old.StagingDir);
                Current.PendingInstalls.Remove(old);
            }
            Current.PendingInstalls.Add(install);
        }
        Save();
    }

    /// <summary>Runs at startup BEFORE any plugin is loaded (nothing is locked yet): removals first, then updates.</summary>
    public static void ApplyPending(string pluginsRoot)
    {
        bool changed = false;

        lock (Gate)
        {
            foreach (var dir in Current.PendingRemoval.ToList())
            {
                string full = Path.Combine(pluginsRoot, dir);
                if (!Directory.Exists(full) || TryDelete(full))
                {
                    Current.PendingRemoval.Remove(dir);
                    changed = true;
                }
            }

            foreach (var inst in Current.PendingInstalls.ToList())
            {
                try
                {
                    string target = Path.Combine(pluginsRoot, inst.TargetDir);
                    if (Directory.Exists(inst.StagingDir))
                    {
                        if (Directory.Exists(target) && !TryDelete(target)) continue;   // keep pending, try next start
                        Directory.CreateDirectory(pluginsRoot);
                        Directory.Move(inst.StagingDir, target);
                    }
                    Current.PendingInstalls.Remove(inst);
                    changed = true;
                }
                catch { }
            }
        }

        if (changed) Save();
    }

    public static bool TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            return true;
        }
        catch { return false; }
    }
}
