using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using YHG_Assets_Handler.Plugins;

namespace YHG_Assets_Handler.ViewModels
{
    /// <summary>One row in the Plugin Manager.</summary>
    public partial class PluginEntry : ObservableObject
    {
        private static readonly Brush Green = Make("#4EC9B0");
        private static readonly Brush Warm = Make("#E0A370");
        private static readonly Brush Red = Make("#EF4444");
        private static readonly Brush Muted = Make("#8A8A93");

        private readonly Action _changed;
        private bool _suppress;

        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Version { get; init; } = "";
        public string Author { get; init; } = "";
        public string Credits { get; init; } = "";
        public string License { get; init; } = "";
        public string Description { get; init; } = "";
        public string DirectoryName { get; init; } = "";
        public string? Error { get; init; }
        public PluginStatus? LoadStatus { get; init; }
        public bool IsInstalledOnly { get; init; }   // present on disk but not seen at startup

        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private Brush _statusBrush = Muted;
        [ObservableProperty] private bool _isEnabled = true;
        [ObservableProperty] private bool _canToggle = true;
        [ObservableProperty] private bool _canUninstall = true;

        public bool HasError => !string.IsNullOrEmpty(Error);
        public string Details => string.Join("  ·  ", new[] { Author, string.IsNullOrEmpty(License) ? "" : "License: " + License }.Where(x => !string.IsNullOrWhiteSpace(x)));

        public PluginEntry(Action changed) => _changed = changed;

        partial void OnIsEnabledChanged(bool value)
        {
            if (_suppress) return;
            PluginStateStore.SetDisabled(Id, !value);
            _changed();
        }

        /// <summary>Recomputes status text from the persisted intentions (state file) vs. what was actually loaded at startup.</summary>
        public void Refresh()
        {
            _suppress = true;
            bool disabledFlag = PluginStateStore.IsDisabled(Id);
            IsEnabled = !disabledFlag;
            _suppress = false;

            bool pendingRemoval = PluginStateStore.IsPendingRemoval(DirectoryName);
            bool pendingUpdate = PluginStateStore.FindPendingInstall(DirectoryName) != null;

            CanToggle = LoadStatus is PluginStatus.Loaded or PluginStatus.Disabled && !pendingRemoval;
            CanUninstall = !pendingRemoval;

            string text;
            Brush brush;

            if (pendingRemoval) { text = "Will be removed on restart"; brush = Warm; }
            else if (IsInstalledOnly) { text = "Installed · restart to activate"; brush = Warm; }
            else switch (LoadStatus)
            {
                case PluginStatus.Loaded:
                    if (disabledFlag) { text = "Will be disabled on restart"; brush = Warm; }
                    else { text = "Loaded"; brush = Green; }
                    break;
                case PluginStatus.Disabled:
                    if (!disabledFlag) { text = "Will be enabled on restart"; brush = Warm; }
                    else { text = "Disabled"; brush = Muted; }
                    break;
                case PluginStatus.Incompatible: text = "Incompatible"; brush = Red; break;
                default: text = "Failed to load"; brush = Red; break;
            }

            if (pendingUpdate && !pendingRemoval) text += " · update pending restart";

            StatusText = text;
            StatusBrush = brush;
        }

        private static Brush Make(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
            b.Freeze();
            return b;
        }
    }
}
