using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AssetsHandler.Controls
{
    public delegate void DarkTreeItemEventHandler(object sender, DarkTreeItemEventArgs e);
    public sealed class DarkTreeItemEventArgs : RoutedEventArgs
    {
        public DarkTreeItemEventArgs(RoutedEvent ev, object source, object? item,
            DarkTreeViewItem_01? container, Point position, MouseButtonEventArgs? mouse) : base(ev, source)
        {
            Item = item; Container = container; Position = position; Mouse = mouse;
        }
        public object? Item { get; }
        public DarkTreeViewItem_01? Container { get; }
        public Point Position { get; }                  
        public MouseButtonEventArgs? Mouse { get; }
        public bool IsOnItem => Item != null;
        public ContextMenu? Menu { get; set; }
    }
    public class DarkTreeView_01 : TreeView
    {
        static DarkTreeView_01()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DarkTreeView_01),
                new FrameworkPropertyMetadata(typeof(DarkTreeView_01)));
        }
        private static DependencyProperty Reg<T>(string n, T? def = default) =>
            DependencyProperty.Register(n, typeof(T), typeof(DarkTreeView_01), new PropertyMetadata(def));
        public static readonly DependencyProperty ChildrenMemberPathProperty = Reg<string>(nameof(ChildrenMemberPath));
        public static readonly DependencyProperty IconKindPathProperty = Reg<string>(nameof(IconKindPath));
        public static readonly DependencyProperty IconKindConverterProperty = Reg<System.Windows.Data.IValueConverter>(nameof(IconKindConverter));
        public static readonly DependencyProperty IconBrushPathProperty = Reg<string>(nameof(IconBrushPath));
        public static readonly DependencyProperty IconBrushConverterProperty = Reg<System.Windows.Data.IValueConverter>(nameof(IconBrushConverter));
        public static readonly DependencyProperty IsExpandedMemberPathProperty = Reg<string>(nameof(IsExpandedMemberPath));
        public static readonly DependencyProperty IsSelectedMemberPathProperty = Reg<string>(nameof(IsSelectedMemberPath));
        public static readonly DependencyProperty ItemContextMenuProperty = Reg<ContextMenu>(nameof(ItemContextMenu));
        public static readonly DependencyProperty ItemClickCommandProperty = Reg<ICommand>(nameof(ItemClickCommand));
        public static readonly DependencyProperty ItemDoubleClickCommandProperty = Reg<ICommand>(nameof(ItemDoubleClickCommand));
        public static readonly DependencyProperty ItemRightClickCommandProperty = Reg<ICommand>(nameof(ItemRightClickCommand));
        public string? ChildrenMemberPath { get => (string?)GetValue(ChildrenMemberPathProperty); set => SetValue(ChildrenMemberPathProperty, value); }
        public string? IconKindPath { get => (string?)GetValue(IconKindPathProperty); set => SetValue(IconKindPathProperty, value); }
        public System.Windows.Data.IValueConverter? IconKindConverter { get => (System.Windows.Data.IValueConverter?)GetValue(IconKindConverterProperty); set => SetValue(IconKindConverterProperty, value); }
        public string? IconBrushPath { get => (string?)GetValue(IconBrushPathProperty); set => SetValue(IconBrushPathProperty, value); }
        public System.Windows.Data.IValueConverter? IconBrushConverter { get => (System.Windows.Data.IValueConverter?)GetValue(IconBrushConverterProperty); set => SetValue(IconBrushConverterProperty, value); }
        public string? IsExpandedMemberPath { get => (string?)GetValue(IsExpandedMemberPathProperty); set => SetValue(IsExpandedMemberPathProperty, value); }
        public string? IsSelectedMemberPath { get => (string?)GetValue(IsSelectedMemberPathProperty); set => SetValue(IsSelectedMemberPathProperty, value); }
        public ContextMenu? ItemContextMenu { get => (ContextMenu?)GetValue(ItemContextMenuProperty); set => SetValue(ItemContextMenuProperty, value); }
        public Func<object, ContextMenu?>? ItemContextMenuProvider { get; set; }
        public ICommand? ItemClickCommand { get => (ICommand?)GetValue(ItemClickCommandProperty); set => SetValue(ItemClickCommandProperty, value); }
        public ICommand? ItemDoubleClickCommand { get => (ICommand?)GetValue(ItemDoubleClickCommandProperty); set => SetValue(ItemDoubleClickCommandProperty, value); }
        public ICommand? ItemRightClickCommand { get => (ICommand?)GetValue(ItemRightClickCommandProperty); set => SetValue(ItemRightClickCommandProperty, value); }
        private static RoutedEvent E(string n) => EventManager.RegisterRoutedEvent(n, RoutingStrategy.Bubble,
            typeof(DarkTreeItemEventHandler), typeof(DarkTreeView_01));
        public static readonly RoutedEvent ItemClickEvent = E(nameof(ItemClick));
        public static readonly RoutedEvent ItemDoubleClickEvent = E(nameof(ItemDoubleClick));
        public static readonly RoutedEvent ItemRightClickEvent = E(nameof(ItemRightClick));
        public static readonly RoutedEvent ItemContextRequestedEvent = E(nameof(ItemContextRequested));
        public static readonly RoutedEvent ContextRequestedEvent = E(nameof(ContextRequested));
        public event DarkTreeItemEventHandler ItemClick { add => AddHandler(ItemClickEvent, value); remove => RemoveHandler(ItemClickEvent, value); }
        public event DarkTreeItemEventHandler ItemDoubleClick { add => AddHandler(ItemDoubleClickEvent, value); remove => RemoveHandler(ItemDoubleClickEvent, value); }
        public event DarkTreeItemEventHandler ItemRightClick { add => AddHandler(ItemRightClickEvent, value); remove => RemoveHandler(ItemRightClickEvent, value); }
        public event DarkTreeItemEventHandler ItemContextRequested { add => AddHandler(ItemContextRequestedEvent, value); remove => RemoveHandler(ItemContextRequestedEvent, value); }
        public event DarkTreeItemEventHandler ContextRequested { add => AddHandler(ContextRequestedEvent, value); remove => RemoveHandler(ContextRequestedEvent, value); }
        protected override DependencyObject GetContainerForItemOverride() => new DarkTreeViewItem_01();
        protected override bool IsItemItsOwnContainerOverride(object item) => item is DarkTreeViewItem_01;
        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);
            if (element is DarkTreeViewItem_01 c) c.Setup(this, item);
        }
        protected override void ClearContainerForItemOverride(DependencyObject element, object item)
        {
            base.ClearContainerForItemOverride(element, item);
            if (element is DarkTreeViewItem_01 c) c.Reset();
        }
        private static readonly object Flag = new();
        private readonly ConditionalWeakTable<object, object> _expanded = new();
        internal bool WasExpanded(object item) => _expanded.TryGetValue(item, out _);
        internal void Remember(object item, bool expanded)
        {
            if (expanded) _expanded.AddOrUpdate(item, Flag);
            else _expanded.Remove(item);
        }
        internal void RaiseItem(RoutedEvent ev, DarkTreeViewItem_01 c, MouseButtonEventArgs m, ICommand? cmd)
        {
            if (c.Data == null) return;
            var a = new DarkTreeItemEventArgs(ev, this, c.Data, c, m.GetPosition(this), m);
            RaiseEvent(a);
            if (!a.Handled && cmd?.CanExecute(c.Data) == true) cmd.Execute(c.Data);
        }
        internal void RaiseItemContext(DarkTreeViewItem_01 c, MouseButtonEventArgs m)
        {
            if (c.Data == null) return;
            var a = new DarkTreeItemEventArgs(ItemContextRequestedEvent, this, c.Data, c, m.GetPosition(this), m)
            {
                Menu = ItemContextMenuProvider?.Invoke(c.Data) ?? ItemContextMenu
            };
            RaiseEvent(a);
            if (a.Handled || a.Menu == null) return;
            a.Menu.DataContext = c.Data;     
            a.Menu.Tag = DataContext;        
            a.Menu.PlacementTarget = c;
            a.Menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            a.Menu.IsOpen = true;
        }
        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);   
            RaiseEvent(new DarkTreeItemEventArgs(ContextRequestedEvent, this, null, null, e.GetPosition(this), e));
        }
    }
}