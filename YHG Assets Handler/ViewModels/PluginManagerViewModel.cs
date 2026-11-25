using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PluginAbstractions;
using YHG_Assets_Handler.Plugins;

namespace YHG_Assets_Handler.ViewModels
{
    public partial class PluginManagerViewModel : ObservableObject
    {
        public ObservableCollection<PluginEntry> Plugins { get; } = new();

        [ObservableProperty] private bool _restartRequired;
        [ObservableProperty] private string _message = "";

        public bool IsEmpty => Plugins.Count == 0;
        public string PluginsFolder => PluginManager.Instance.PluginsRoot;

        public PluginManagerViewModel()
        {
            Rebuild();
        }

        private void OnEntryChanged()
        {
            RestartRequired = true;
            foreach (var p in Plugins) p.Refresh();
        }

        private void Rebuild()
        {
            Plugins.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // What the host saw at startup
            foreach (var r in PluginManager.Instance.Results.OrderBy(r => r.Info?.Name ?? r.Directory))
            {
                string dirName = Path.GetFileName(r.Directory);
                seen.Add(dirName);
                Plugins.Add(Create(r.Info, dirName, r.Status, r.Error, installedOnly: false));
            }

            // Folders that appeared after startup (freshly installed)
            var root = PluginManager.Instance.PluginsRoot;
            if (Directory.Exists(root))
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    string dirName = Path.GetFileName(dir);
                    if (seen.Contains(dirName)) continue;
                    var m = PluginManager.ReadManifest(dir);
                    if (m == null) continue;
                    var info = new PluginInfo { Id = m.Id, Name = m.Name, Version = m.Version };
                    Plugins.Add(Create(info, dirName, null, null, installedOnly: true));
                }
            }

            foreach (var p in Plugins) p.Refresh();
            OnPropertyChanged(nameof(IsEmpty));
        }

        private PluginEntry Create(PluginInfo? info, string dirName, PluginStatus? status, string? error, bool installedOnly) => new(OnEntryChanged)
        {
            Id = info?.Id ?? dirName,
            Name = string.IsNullOrWhiteSpace(info?.Name) ? dirName : info!.Name,
            Version = info?.Version ?? "",
            Author = info?.Author ?? "",
            Credits = info?.Credits ?? "",
            License = info?.License ?? "",
            Description = info?.Description ?? "",
            DirectoryName = dirName,
            Error = error,
            LoadStatus = status,
            IsInstalledOnly = installedOnly
        };

        [RelayCommand]
        private void Install()
        {
            var ofd = new OpenFileDialog
            {
                Title = "Install plugin",
                Filter = "Plugin package (*.zip)|*.zip",
                Multiselect = true
            };
            if (ofd.ShowDialog() != true) return;

            var lines = new List<string>();
            bool any = false;
            foreach (var file in ofd.FileNames)
            {
                var result = PluginInstaller.Install(file);
                lines.Add($"{Path.GetFileName(file)}: {result.Message}");
                any |= result.Success;
            }

            Message = string.Join("\n", lines);
            if (any) RestartRequired = true;
            Rebuild();
        }

        [RelayCommand]
        private void Uninstall(PluginEntry? entry)
        {
            if (entry == null) return;

            bool ok = Dialogs.Current.Confirm("Uninstall plugin",
                $"Uninstall '{entry.Name}'?\n\nThe plugin stays active until you restart; its files are removed on the next start.");
            if (!ok) return;

            PluginStateStore.SetPendingRemoval(entry.DirectoryName, true);
            RestartRequired = true;
            Message = $"'{entry.Name}' will be removed on restart.";
            foreach (var p in Plugins) p.Refresh();
        }

        [RelayCommand]
        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(PluginsFolder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{PluginsFolder}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Message = $"Could not open folder: {ex.Message}";
            }
        }

        [RelayCommand]
        private void Restart()
        {
            if (Application.Current?.MainWindow is MainWindow mw)
                mw.RequestRestart();
        }
    }
}
