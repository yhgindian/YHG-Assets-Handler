using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.Views.Controls.Tree;
using YHG_Assets_Handler.Views.Windows.Unity;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.Views.Controls
{
    public class AssetDataTreeView : CodeDataTreeView
    {
        private UnityAssetWorkspace? _workspace;
        private readonly ContextMenu _linkMenu;
        private readonly MenuItem _openItem;
        private readonly MenuItem _copyItem;
        private readonly MenuItem _resolveDepItem;
        private readonly Separator _menuSeparator;

        public AssetDataTreeView()
        {
            _linkMenu = new ContextMenu { Background = Palette.MenuBg, Foreground = Palette.Field };

            _openItem = new MenuItem { Header = "Open In New Inspector Window" };
            _openItem.Click += (_, _) =>
            {
                if (SelectedItem is FieldNode { Tag: UnityNodePayload { IsLink: true, IsUnresolvedDep: false } payload } && _workspace != null)
                {
                    var target = _workspace.GetAssetContainer(payload.FromFile, payload.FileId, payload.PathId);
                    if (target != null) new UnityAssetDataViewer(_workspace, target).Show();
                }
            };

            _copyItem = new MenuItem { Header = "Copy (FileID, PathID)" };
            _copyItem.Click += (_, _) =>
            {
                if (SelectedItem is FieldNode { Tag: UnityNodePayload payload })
                {
                    try { Clipboard.SetText($"FileID: {payload.FileId}, PathID: {payload.PathId}"); } catch { }
                }
            };

            _menuSeparator = new Separator { Background = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)) };

            _resolveDepItem = new MenuItem { Header = "Resolve Dependency..." };
            _resolveDepItem.Click += async (_, _) =>
            {
                if (SelectedItem is FieldNode node && node.Tag is UnityNodePayload payload)
                {
                    await HandleResolveDependencyAsync(node, payload);
                }
            };

            _linkMenu.Items.Add(_openItem);
            _linkMenu.Items.Add(_copyItem);
            _linkMenu.Items.Add(_menuSeparator);
            _linkMenu.Items.Add(_resolveDepItem);

            ContextMenu = _linkMenu;

            ContextMenuOpening += (_, e) =>
            {
                if (SelectedItem is not FieldNode { Tag: UnityNodePayload payload } || (!payload.IsLink && !payload.IsUnresolvedDep))
                {
                    e.Handled = true;
                    return;
                }

                if (payload.IsUnresolvedDep)
                {
                    _openItem.Visibility = Visibility.Collapsed;
                    _copyItem.Visibility = Visibility.Visible;
                    _menuSeparator.Visibility = Visibility.Visible;
                    _resolveDepItem.Visibility = Visibility.Visible;
                }
                else
                {
                    _openItem.Visibility = Visibility.Visible;
                    _copyItem.Visibility = Visibility.Visible;
                    _menuSeparator.Visibility = Visibility.Collapsed;
                    _resolveDepItem.Visibility = Visibility.Collapsed;
                }
            };
        }

        public void Init(UnityAssetWorkspace workspace)
        {
            _workspace = workspace;
            Clear();
        }

        public async Task LoadComponentAsync(AssetContainer container)
        {
            if (_workspace == null || container.FileInstance == null) return;
            Clear();

            FieldNode root = await Task.Run(() => UnityTreeBuilder.BuildRoot(_workspace, container));
            SetRootNode(root);
        }

        private async Task HandleResolveDependencyAsync(FieldNode node, UnityNodePayload payload)
        {
            if (_workspace == null || payload.FromFile == null) return;

            try
            {
                var target = await Task.Run(() =>
                    _workspace.GetAssetContainer(payload.FromFile, payload.FileId, payload.PathId, forceLoadDep: true));

                var baseField = _workspace.GetBaseField(target);
                if (target != null && baseField != null)
                {
                    payload.FromFile = target.FileInstance;
                    payload.IsUnresolvedDep = false;
                    payload.IsLink = true;

                    node.TypeText = baseField.TypeName;
                    node.TypeBrush = Palette.Type;
                    node.NameText = " " + baseField.FieldName;
                    node.NameBrush = Palette.Field;
                    node.Middle = "";
                    node.Value = "";
                    node.Comment = "";
                    node.Weight = FontWeights.SemiBold;
                    node.NodeCursor = null;

                    node.SetChildren(UnityTreeBuilder.BuildChildren(_workspace, target.FileInstance, baseField, payload.PathId));
                    node.IsExpanded = true;
                }
                else
                {
                    NotificationManager.Error("Dependency resolved, but PathID could not be decoded.");
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Error($"Failed to resolve dependency: {ex.Message}");
            }
        }
    }
}