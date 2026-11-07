using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace YHG_Assets_Handler.Views.Controls.Tree
{
    public static class Palette
    {
        private static SolidColorBrush Make(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        public static readonly SolidColorBrush Type = Make(0x4E, 0xC9, 0xB0);
        public static readonly SolidColorBrush Primitive = Make(0x56, 0x9C, 0xD6);
        public static readonly SolidColorBrush Field = Make(0xDC, 0xDC, 0xDC);
        public static readonly SolidColorBrush String = Make(0xCE, 0x91, 0x78);
        public static readonly SolidColorBrush Value = Make(0xB5, 0xCE, 0xA8);
        public static readonly SolidColorBrush Link = Make(0x4E, 0xC9, 0xB0);
        public static readonly SolidColorBrush Warning = Make(0xCE, 0x91, 0x78);
        public static readonly SolidColorBrush MenuBg = Make(0x22, 0x22, 0x25);
        public static readonly SolidColorBrush TreeBg = Make(0x18, 0x18, 0x18);
    }

    public class FieldNode : INotifyPropertyChanged
    {
        private static readonly IReadOnlyList<FieldNode> Empty = Array.Empty<FieldNode>();
        private static readonly IReadOnlyList<FieldNode> LoadingPlaceholder = new[] { new FieldNode() };

        private IReadOnlyList<FieldNode> _children = Empty;
        private Func<List<FieldNode>>? _loader;
        private bool _isExpanded;

        private string _typeText = "";
        private Brush _typeBrush = Palette.Field;
        private string _nameText = "";
        private Brush _nameBrush = Palette.Field;
        private string _middle = "";
        private string _value = "";
        private Brush _valueBrush = Palette.Value;
        private string _comment = "";
        private FontWeight _weight = FontWeights.Normal;
        private Cursor? _nodeCursor;

        // Any format payload (UnityNodePayload, SiiTokenPayload, offset info, etc.)
        public object? Tag { get; set; }

        public string TypeText
        {
            get => _typeText;
            set { if (_typeText != value) { _typeText = value; OnChanged(); } }
        }

        public Brush TypeBrush
        {
            get => _typeBrush;
            set { if (_typeBrush != value) { _typeBrush = value; OnChanged(); } }
        }

        public string NameText
        {
            get => _nameText;
            set { if (_nameText != value) { _nameText = value; OnChanged(); } }
        }

        public Brush NameBrush
        {
            get => _nameBrush;
            set { if (_nameBrush != value) { _nameBrush = value; OnChanged(); } }
        }

        public string Middle
        {
            get => _middle;
            set { if (_middle != value) { _middle = value; OnChanged(); } }
        }

        public string Value
        {
            get => _value;
            set { if (_value != value) { _value = value; OnChanged(); } }
        }

        public Brush ValueBrush
        {
            get => _valueBrush;
            set { if (_valueBrush != value) { _valueBrush = value; OnChanged(); } }
        }

        public string Comment
        {
            get => _comment;
            set { if (_comment != value) { _comment = value; OnChanged(); } }
        }

        public FontWeight Weight
        {
            get => _weight;
            set { if (_weight != value) { _weight = value; OnChanged(); } }
        }

        public Cursor? NodeCursor
        {
            get => _nodeCursor;
            set { if (_nodeCursor != value) { _nodeCursor = value; OnChanged(); } }
        }

        public IReadOnlyList<FieldNode> Children => _children;

        public static FieldNode Label(string text, Brush? brush = null) => new()
        {
            NameText = text,
            NameBrush = brush ?? Palette.Field
        };

        public void SetChildren(List<FieldNode> list)
        {
            _loader = null;
            _children = list.Count == 0 ? Empty : list;
            OnChanged(nameof(Children));
        }

        public void SetLazy(Func<List<FieldNode>> loader)
        {
            _loader = loader;
            _children = LoadingPlaceholder;
            OnChanged(nameof(Children));
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;

                if (value && _loader != null)
                {
                    var loader = _loader;
                    _loader = null;
                    List<FieldNode> result;
                    try { result = loader(); }
                    catch (Exception ex) { result = new List<FieldNode> { Label($"[error: {ex.Message}]", Palette.Warning) }; }

                    _children = result.Count == 0 ? Empty : result;
                    OnChanged(nameof(Children));
                }
                OnChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CodeDataTreeView : TreeView
    {
        public CodeDataTreeView()
        {
            Background = Palette.TreeBg;
            BorderThickness = new Thickness(0);
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New");
            FontSize = 12.5;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            VirtualizingPanel.SetIsVirtualizing(this, true);
            VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);
            VirtualizingPanel.SetScrollUnit(this, ScrollUnit.Pixel);
            ScrollViewer.SetCanContentScroll(this, true);

            ItemTemplate = CreateItemTemplate();

            var style = new Style(typeof(TreeViewItem));
            style.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty,
                new Binding(nameof(FieldNode.IsExpanded)) { Mode = BindingMode.TwoWay }));
            style.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
            ItemContainerStyle = style;

            PreviewMouseRightButtonDown += (_, e) =>
            {
                if (FindItem(e.OriginalSource as DependencyObject) is { } item)
                    item.IsSelected = true;
            };
        }

        public void SetRootNode(FieldNode root)
        {
            ItemsSource = new[] { root };
        }

        public void Clear()
        {
            ItemsSource = null;
        }

        public static TreeViewItem? FindItem(DependencyObject? d)
        {
            while (d != null && d is not TreeViewItem)
            {
                d = d is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return d as TreeViewItem;
        }

        private static HierarchicalDataTemplate CreateItemTemplate()
        {
            const string xaml = @"
<HierarchicalDataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                          ItemsSource='{Binding Children}'>
    <TextBlock Padding='2,1,2,1' TextWrapping='NoWrap'
               Cursor='{Binding NodeCursor}'>
        <Run Text='{Binding TypeText}'
             Foreground='{Binding TypeBrush}'
             FontWeight='{Binding Weight}'/><Run Text='{Binding NameText}'
             Foreground='{Binding NameBrush}'
             FontWeight='{Binding Weight}'/><Run Text='{Binding Middle}'
             Foreground='#808080'/><Run Text='{Binding Value}'
             Foreground='{Binding ValueBrush}'/><Run Text='{Binding Comment}'
             Foreground='#808080'/>
    </TextBlock>
</HierarchicalDataTemplate>";
            return (HierarchicalDataTemplate)XamlReader.Parse(xaml);
        }
    }
}