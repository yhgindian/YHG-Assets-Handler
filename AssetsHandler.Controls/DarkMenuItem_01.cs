using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace AssetsHandler.Controls
{
    public class DarkMenuItem_01 : MenuItem
    {
        static DarkMenuItem_01()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DarkMenuItem_01),
                new FrameworkPropertyMetadata(typeof(DarkMenuItem_01)));
        }
        public static readonly DependencyProperty IconKindProperty = DependencyProperty.Register(
            nameof(IconKind), typeof(PackIconMaterialKind), typeof(DarkMenuItem_01),
            new PropertyMetadata(PackIconMaterialKind.None));
        public static readonly DependencyProperty IconBrushProperty = DependencyProperty.Register(
            nameof(IconBrush), typeof(Brush), typeof(DarkMenuItem_01), new PropertyMetadata(null));
        public PackIconMaterialKind IconKind { get => (PackIconMaterialKind)GetValue(IconKindProperty); set => SetValue(IconKindProperty, value); }
        public Brush? IconBrush { get => (Brush?)GetValue(IconBrushProperty); set => SetValue(IconBrushProperty, value); }
        public static readonly DependencyProperty VisibleSourceProperty = DependencyProperty.Register(
            nameof(VisibleSource), typeof(object), typeof(DarkMenuItem_01),
            new PropertyMetadata(null, (d, _) => ((DarkMenuItem_01)d).UpdateVisibility()));
        public static readonly DependencyProperty VisibleForProperty = DependencyProperty.Register(
            nameof(VisibleFor), typeof(string), typeof(DarkMenuItem_01),
            new PropertyMetadata(null, (d, e) => ((DarkMenuItem_01)d).OnVisibleForChanged((string?)e.NewValue)));
        public object? VisibleSource { get => GetValue(VisibleSourceProperty); set => SetValue(VisibleSourceProperty, value); }
        public string? VisibleFor { get => (string?)GetValue(VisibleForProperty); set => SetValue(VisibleForProperty, value); }
        private string[] _tokens = Array.Empty<string>();
        private void OnVisibleForChanged(string? v)
        {
            _tokens = string.IsNullOrWhiteSpace(v)
                ? Array.Empty<string>()
                : v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            UpdateVisibility();
        }
        private void UpdateVisibility()
        {
            if (_tokens.Length == 0) return;   
            string? s = VisibleSource?.ToString();
            bool visible;
            if (s == null) visible = false;    
            else if (_tokens.Any(t => t.StartsWith('!')))
                visible = !_tokens.Any(t => t.StartsWith('!') && Eq(t[1..], s));
            else
                visible = _tokens.Any(t => Eq(t, s));
            SetCurrentValue(VisibilityProperty, visible ? Visibility.Visible : Visibility.Collapsed);
        }
        private static bool Eq(string a, string b) => string.Equals(a.Trim(), b, StringComparison.OrdinalIgnoreCase);
        protected override DependencyObject GetContainerForItemOverride() => new DarkMenuItem_01();
        protected override bool IsItemItsOwnContainerOverride(object item) => item is MenuItem or Separator;
    }
}