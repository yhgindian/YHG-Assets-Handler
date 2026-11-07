using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace AssetsHandler.Controls
{
    public class DarkTreeViewItem_01 : TreeViewItem
    {
        static DarkTreeViewItem_01()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DarkTreeViewItem_01),
                new FrameworkPropertyMetadata(typeof(DarkTreeViewItem_01)));
        }
        public static readonly DependencyProperty IconKindProperty = DependencyProperty.Register(
            nameof(IconKind), typeof(PackIconMaterialKind), typeof(DarkTreeViewItem_01),
            new PropertyMetadata(PackIconMaterialKind.None));
        public static readonly DependencyProperty IconBrushProperty = DependencyProperty.Register(
            nameof(IconBrush), typeof(Brush), typeof(DarkTreeViewItem_01), new PropertyMetadata(null));
        public PackIconMaterialKind IconKind { get => (PackIconMaterialKind)GetValue(IconKindProperty); set => SetValue(IconKindProperty, value); }
        public Brush? IconBrush { get => (Brush?)GetValue(IconBrushProperty); set => SetValue(IconBrushProperty, value); }
        public DarkTreeView_01? Tree { get; internal set; }
        internal object? Data;
        private bool _down;
        protected override DependencyObject GetContainerForItemOverride() => new DarkTreeViewItem_01();
        protected override bool IsItemItsOwnContainerOverride(object item) => item is DarkTreeViewItem_01;
        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);
            if (element is DarkTreeViewItem_01 c) c.Setup(Tree, item);
        }
        protected override void ClearContainerForItemOverride(DependencyObject element, object item)
        {
            base.ClearContainerForItemOverride(element, item);
            if (element is DarkTreeViewItem_01 c) c.Reset();
        }
        internal void Setup(DarkTreeView_01? tree, object item)
        {
            Tree = tree; Data = item;
            if (tree == null) return;
            if (tree.ItemTemplate == null && tree.ItemTemplateSelector == null)
                SetBinding(HeaderProperty, Make(item, tree.DisplayMemberPath, null));
            if (!string.IsNullOrEmpty(tree.ChildrenMemberPath))
                SetBinding(ItemsSourceProperty, Make(item, tree.ChildrenMemberPath, null));
            if (!string.IsNullOrEmpty(tree.IconKindPath) || tree.IconKindConverter != null)
                SetBinding(IconKindProperty, Make(item, tree.IconKindPath, tree.IconKindConverter));
            if (!string.IsNullOrEmpty(tree.IconBrushPath) || tree.IconBrushConverter != null)
                SetBinding(IconBrushProperty, Make(item, tree.IconBrushPath, tree.IconBrushConverter));
            if (!string.IsNullOrEmpty(tree.IsExpandedMemberPath))
                SetBinding(IsExpandedProperty, Make(item, tree.IsExpandedMemberPath, null, BindingMode.TwoWay));
            else if (tree.WasExpanded(item))
                SetCurrentValue(IsExpandedProperty, true);
            if (!string.IsNullOrEmpty(tree.IsSelectedMemberPath))
                SetBinding(IsSelectedProperty, Make(item, tree.IsSelectedMemberPath, null, BindingMode.TwoWay));
            else if (ReferenceEquals(tree.SelectedItem, item))
                SetCurrentValue(IsSelectedProperty, true);
        }
        internal void Reset()
        {
            BindingOperations.ClearBinding(this, IconKindProperty);
            BindingOperations.ClearBinding(this, IconBrushProperty);
            BindingOperations.ClearBinding(this, ItemsSourceProperty);
            BindingOperations.ClearBinding(this, HeaderProperty);
            Tree = null; Data = null;
        }
        private static Binding Make(object item, string? path, IValueConverter? conv, BindingMode mode = BindingMode.OneWay)
        {
            var b = new Binding { Source = item, Converter = conv, Mode = mode };
            if (!string.IsNullOrEmpty(path)) b.Path = new PropertyPath(path);
            return b;
        }
        protected override void OnExpanded(RoutedEventArgs e) { base.OnExpanded(e); if (Data != null) Tree?.Remember(Data, true); }
        protected override void OnCollapsed(RoutedEventArgs e) { base.OnCollapsed(e); if (Data != null) Tree?.Remember(Data, false); }
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if (GetTemplateChild("PART_Header") is not FrameworkElement h) return;
            h.MouseLeftButtonDown += (_, e) =>
            {
                _down = true;
                if (e.ClickCount == 2) Tree?.RaiseItem(DarkTreeView_01.ItemDoubleClickEvent, this, e, Tree.ItemDoubleClickCommand);
            };
            h.MouseLeftButtonUp += (_, e) =>
            {
                if (!_down) return;
                _down = false;
                Tree?.RaiseItem(DarkTreeView_01.ItemClickEvent, this, e, Tree.ItemClickCommand);
            };
            h.MouseLeave += (_, _) => _down = false;
            h.MouseRightButtonDown += (_, _) =>
            {
                if (!IsSelected) SetCurrentValue(IsSelectedProperty, true);
                Focus();
            };
            h.MouseRightButtonUp += (_, e) =>
            {
                if (Tree == null) return;
                Tree.RaiseItem(DarkTreeView_01.ItemRightClickEvent, this, e, Tree.ItemRightClickCommand);
                Tree.RaiseItemContext(this, e);
                e.Handled = true;
            };
        }
    }
}