using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using YHG.FastBF.Binary;

public sealed class Settings
{
    private static readonly Lazy<Settings> _instance = new(() => new Settings());
    public static Settings Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _ioLock = new();

    private DateTime _lastKnownWriteTimeUtc = DateTime.MinValue;
    private readonly Timer _debounceTimer;
    private const int DebounceDelayMs = 300;

    private Settings()
    {
        _debounceTimer = new Timer(_ => SaveImmediate(), null, Timeout.Infinite, Timeout.Infinite);
        Load(forceReload: true);
    }

    /// <summary>
    /// Indexer providing fast in-memory access to setting values.
    /// </summary>
    public SettingEntry this[string key]
    {
        get => new(key, _store.TryGetValue(key, out var val) ? val : null);
        set => Set(key, value.RawValue ?? string.Empty);
    }

    public Settings Set<T>(string key, T value)
    {
        if (value is null)
        {
            _store[key] = string.Empty;
            ScheduleSave();
            return this;
        }

        string formatted = value switch
        {
            string s => s,
            bool b => b ? "true" : "false",
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString("c", CultureInfo.InvariantCulture),
            DateOnly dOnly => dOnly.ToString("O", CultureInfo.InvariantCulture),
            TimeOnly tOnly => tOnly.ToString("O", CultureInfo.InvariantCulture),
            Guid g => g.ToString("D"),
            byte[] bytes => Convert.ToHexString(bytes),
            Enum e => e.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => JsonSerializer.Serialize(value)
        };

        _store[key] = formatted;
        ScheduleSave();
        return this;
    }

    public bool Has(string key) => _store.ContainsKey(key);

    public bool Remove(string key)
    {
        if (_store.TryRemove(key, out _))
        {
            ScheduleSave();
            return true;
        }
        return false;
    }

    public void Clear()
    {
        _store.Clear();
        ScheduleSave();
    }

    public IReadOnlyDictionary<string, string> GetAll() => _store;

    private void ScheduleSave()
    {
        // Debounce write requests; resets the 300ms countdown on burst updates
        _debounceTimer.Change(DebounceDelayMs, Timeout.Infinite);
    }

    public void Flush()
    {
        _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
        SaveImmediate();
    }

    public void SaveImmediate()
    {
        lock (_ioLock)
        {
            try
            {
                string targetPath = AppPathResolver.SettingsFile;
                string? dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var data = new SettingsData
                {
                    Values = new Dictionary<string, string>(_store, StringComparer.OrdinalIgnoreCase)
                };

                string tempFile = targetPath + ".tmp";
                using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BfWriter(fs))
                {
                    writer.WriteRootClass(data);
                }

                File.Move(tempFile, targetPath, overwrite: true);
                _lastKnownWriteTimeUtc = File.GetLastWriteTimeUtc(targetPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Binary Save Error: {ex.Message}");
            }
        }
    }

    public void Load(bool forceReload = false)
    {
        lock (_ioLock)
        {
            string targetPath = AppPathResolver.SettingsFile;
            if (!File.Exists(targetPath)) return;

            try
            {
                DateTime diskWriteTimeUtc = File.GetLastWriteTimeUtc(targetPath);
                if (!forceReload && diskWriteTimeUtc == _lastKnownWriteTimeUtc)
                    return;

                using var fs = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new BfReader(fs);

                var loadedData = reader.ReadRootClass<SettingsData>();
                if (loadedData?.Values != null)
                {
                    _store.Clear();
                    foreach (var (k, v) in loadedData.Values)
                    {
                        if (k != null)
                            _store[k] = v ?? string.Empty;
                    }
                }

                _lastKnownWriteTimeUtc = diskWriteTimeUtc;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Binary Load Error: {ex.Message}");
            }
        }
    }
}

[Serializable]
public sealed class SettingsData
{
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}