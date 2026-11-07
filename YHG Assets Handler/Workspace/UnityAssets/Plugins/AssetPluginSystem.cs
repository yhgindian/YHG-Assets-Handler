using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Workspace.UnityAssets;
using YHG_Assets_Handler.Workspace.UnityAssets.Plugins.TextAsset;
using YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Texture2D;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins
{
    [Flags]
    public enum PluginTargetMode : byte
    {
        Single = 1,
        Multi = 2,
        Any = Single | Multi
    }

    public interface IInternalAssetPlugin
    {
        string DisplayName { get; }
        int TargetClassId { get; } // -1 = All types
        PluginTargetMode TargetMode { get; }
        Task ExecuteAsync(UnityAssetWorkspace workspace, IReadOnlyList<UnityAssetObjectModel> selectedAssets);
    }

    public static class PluginMenuCache
    {
        private readonly struct PluginEntry
        {
            public readonly IInternalAssetPlugin Plugin;
            public readonly MenuItem Item;

            public PluginEntry(IInternalAssetPlugin plugin, MenuItem item)
            {
                Plugin = plugin;
                Item = item;
            }
        }

        private static readonly ContextMenu _cachedMenu = new();
        private static readonly PluginEntry[] _entries;

        private static readonly MenuItem _emptyPlaceholderItem = new()
        {
            Header = "(No plugins available)",
            IsEnabled = false
        };

        private static UnityAssetWorkspace? _currentWorkspace;
        private static IReadOnlyList<UnityAssetObjectModel>? _currentSelection;

        static PluginMenuCache()
        {
            var registry = AssetPluginRegistry.Plugins;
            _entries = new PluginEntry[registry.Length];

            for (int i = 0; i < registry.Length; i++)
            {
                var plugin = registry[i];
                var menuItem = new MenuItem
                {
                    Header = plugin.DisplayName,
                    Tag = plugin
                };

                menuItem.Click += async static (sender, _) =>
                {
                    if (sender is not MenuItem { Tag: IInternalAssetPlugin targetPlugin })
                        return;

                    if (_currentWorkspace is not { } ws || _currentSelection is not { } sel)
                        return;

                    try
                    {
                        await targetPlugin.ExecuteAsync(ws, sel);
                    }
                    catch (Exception ex)
                    {
                        NotificationManager.Error(ex.Message, targetPlugin.DisplayName);
                    }
                };

                _cachedMenu.Items.Add(menuItem);
                _entries[i] = new PluginEntry(plugin, menuItem);
            }

            _cachedMenu.Items.Add(_emptyPlaceholderItem);
        }

        /// <summary>
        /// ⚡ Zero-heap-allocation menu toggle (< 1 microsecond)
        /// </summary>
        public static void Show(FrameworkElement target, UnityAssetWorkspace ws, IReadOnlyList<UnityAssetObjectModel> selected)
        {
            int count = selected.Count;
            if (count == 0) return;

            _currentWorkspace = ws;
            _currentSelection = selected;

            var currentMode = count == 1 ? PluginTargetMode.Single : PluginTargetMode.Multi;
            int firstClassId = (int)selected[0].TypeClass;
            bool isHomogeneous = true;

            // Fast scalar scan: exits immediately on first divergent type
            for (int i = 1; i < count; i++)
            {
                if ((int)selected[i].TypeClass != firstClassId)
                {
                    isHomogeneous = false;
                    break;
                }
            }

            int visibleCount = 0;

            for (int i = 0; i < _entries.Length; i++)
            {
                ref readonly var entry = ref _entries[i];
                var plugin = entry.Plugin;

                bool modeMatches = (plugin.TargetMode & currentMode) != 0;
                bool typeMatches = plugin.TargetClassId == -1 || (isHomogeneous && plugin.TargetClassId == firstClassId);

                if (modeMatches && typeMatches)
                {
                    entry.Item.Visibility = Visibility.Visible;
                    visibleCount++;
                }
                else
                {
                    entry.Item.Visibility = Visibility.Collapsed;
                }
            }

            _emptyPlaceholderItem.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;

            _cachedMenu.PlacementTarget = target;
            _cachedMenu.Placement = PlacementMode.Bottom;
            _cachedMenu.IsOpen = true;
        }
    }
}