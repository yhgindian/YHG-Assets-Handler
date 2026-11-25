using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using PluginAbstractions;

namespace YHG_Assets_Handler.Plugins;

public readonly record struct InstallResult(bool Success, string Message);

/// <summary>Validates and installs a plugin .zip (plugin.json at the zip root or inside one top-level folder).</summary>
public static class PluginInstaller
{
    private static readonly Regex IdRegex = new(@"^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.Compiled);

    public static InstallResult Install(string zipPath)
    {
        string? staging = null;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);

            var entries = zip.Entries
                .Where(e => !string.IsNullOrEmpty(e.Name))
                .Select(e => (Entry: e, Path: e.FullName.Replace('\\', '/')))
                .ToList();

            var hit = entries
                .Where(e => e.Path.Equals("plugin.json", StringComparison.OrdinalIgnoreCase)
                            || (e.Path.Count(c => c == '/') == 1 && e.Path.EndsWith("/plugin.json", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(e => e.Path.Count(c => c == '/'))
                .FirstOrDefault();

            if (hit.Entry == null)
                return new(false, "plugin.json not found (it must be at the zip root or inside a single top-level folder).");

            string prefix = hit.Path[..^"plugin.json".Length];   // "" or "Folder/"

            PluginManifest? manifest;
            using (var s = hit.Entry.Open())
                manifest = System.Text.Json.JsonSerializer.Deserialize<PluginManifest>(s);

            if (manifest == null) return new(false, "plugin.json is empty or invalid.");
            if (string.IsNullOrEmpty(manifest.Id) || !IdRegex.IsMatch(manifest.Id)) return new(false, "Invalid plugin id. Use lowercase letters, digits, '.', '-' or '_' (e.g. vendor.format).");
            if (string.IsNullOrWhiteSpace(manifest.Name)) return new(false, "plugin.json: 'name' is required.");
            if (string.IsNullOrWhiteSpace(manifest.Version)) return new(false, "plugin.json: 'version' is required.");
            if (string.IsNullOrWhiteSpace(manifest.EntryAssembly)) return new(false, "plugin.json: 'entryAssembly' is required.");
            if (manifest.MinHostApiVersion > PluginApi.Version)
                return new(false, $"Plugin needs plugin API v{manifest.MinHostApiVersion}, this app provides v{PluginApi.Version}. Update the app first.");

            bool hasEntry = entries.Any(e => string.Equals(e.Path, prefix + manifest.EntryAssembly, StringComparison.OrdinalIgnoreCase));
            if (!hasEntry) return new(false, $"Entry assembly '{manifest.EntryAssembly}' is not in the zip.");

            // Extract to staging (zip-slip safe)
            staging = Path.Combine(AppPathResolver.AppLocalDataDir, "PluginStaging", manifest.Id + "_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(staging);
            string stagingFull = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;

            foreach (var (entry, path) in entries)
            {
                if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string rel = path[prefix.Length..];
                if (rel.Length == 0) continue;

                string dest = Path.GetFullPath(Path.Combine(staging, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!dest.StartsWith(stagingFull, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unsafe path in zip: {path}");

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }

            // Same id already installed under some folder? Then this is an update of that folder.
            string pluginsRoot = PluginManager.Instance.PluginsRoot;
            Directory.CreateDirectory(pluginsRoot);
            string targetDir = FindFolderById(pluginsRoot, manifest.Id) ?? manifest.Id;
            string target = Path.Combine(pluginsRoot, targetDir);

            PluginStateStore.SetPendingRemoval(targetDir, false);

            if (!Directory.Exists(target))
            {
                Directory.Move(staging, target);
                staging = null;
                return new(true, $"'{manifest.Name}' {manifest.Version} installed. Restart to activate it.");
            }

            PluginStateStore.QueueInstall(new PendingInstall { Id = manifest.Id, StagingDir = staging, TargetDir = targetDir });
            staging = null;
            return new(true, $"'{manifest.Name}' {manifest.Version} will replace the installed version on restart.");
        }
        catch (Exception ex)
        {
            return new(false, $"Install failed: {ex.Message}");
        }
        finally
        {
            if (staging != null) PluginStateStore.TryDelete(staging);
        }
    }

    private static string? FindFolderById(string pluginsRoot, string id)
    {
        foreach (var dir in Directory.EnumerateDirectories(pluginsRoot))
        {
            var m = PluginManager.ReadManifest(dir);
            if (m != null && string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(dir);
        }
        return null;
    }
}
