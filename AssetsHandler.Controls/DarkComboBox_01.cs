using System.Collections.Concurrent;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AssetsHandler.Controls
{
    public class DarkComboBox_01 : ComboBox
    {
        static DarkComboBox_01()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DarkComboBox_01),
                new FrameworkPropertyMetadata(typeof(DarkComboBox_01)));
        }
        private static DependencyProperty Reg<T>(string name, T def) =>
            DependencyProperty.Register(name, typeof(T), typeof(DarkComboBox_01), new PropertyMetadata(def));
        private static DependencyProperty Inh(string name) =>
            DependencyProperty.RegisterAttached(name, typeof(Brush), typeof(DarkComboBox_01),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
        public static readonly DependencyProperty PlaceholderProperty = Reg(nameof(Placeholder), "Select...");
        public static readonly DependencyProperty PlaceholderForegroundProperty = Reg<Brush?>(nameof(PlaceholderForeground), null);
        public static readonly DependencyProperty OnHoverBgProperty = Reg<Brush?>(nameof(OnHoverBg), null);
        public static readonly DependencyProperty OnHoverBorderProperty = Reg<Brush?>(nameof(OnHoverBorder), null);
        public static readonly DependencyProperty OnHoverForegroundProperty = Reg<Brush?>(nameof(OnHoverForeground), null);
        public static readonly DependencyProperty DropDownBgProperty = Reg<Brush?>(nameof(DropDownBg), null);
        public static readonly DependencyProperty DropDownBorderProperty = Reg<Brush?>(nameof(DropDownBorder), null);
        public static readonly DependencyProperty CornerRadiusProperty = Reg(nameof(CornerRadius), new CornerRadius(4));
        public static readonly DependencyProperty IsSearchEnabledProperty = Reg(nameof(IsSearchEnabled), true);
        public static readonly DependencyProperty SearchPlaceholderProperty = Reg(nameof(SearchPlaceholder), "Search...");
        public static readonly DependencyProperty NoResultsTextProperty = Reg(nameof(NoResultsText), "No results");
        public static readonly DependencyProperty SearchMemberPathProperty = Reg<string?>(nameof(SearchMemberPath), null);
        public static readonly DependencyProperty ItemHoverBgProperty = Inh("ItemHoverBg");
        public static readonly DependencyProperty ItemHoverForegroundProperty = Inh("ItemHoverForeground");
        public static readonly DependencyProperty ItemSelectedBgProperty = Inh("ItemSelectedBg");
        public static readonly DependencyProperty ItemSelectedForegroundProperty = Inh("ItemSelectedForeground");
        public static Brush? GetItemHoverBg(DependencyObject o) => (Brush?)o.GetValue(ItemHoverBgProperty);
        public static void SetItemHoverBg(DependencyObject o, Brush? v) => o.SetValue(ItemHoverBgProperty, v);
        public static Brush? GetItemHoverForeground(DependencyObject o) => (Brush?)o.GetValue(ItemHoverForegroundProperty);
        public static void SetItemHoverForeground(DependencyObject o, Brush? v) => o.SetValue(ItemHoverForegroundProperty, v);
        public static Brush? GetItemSelectedBg(DependencyObject o) => (Brush?)o.GetValue(ItemSelectedBgProperty);
        public static void SetItemSelectedBg(DependencyObject o, Brush? v) => o.SetValue(ItemSelectedBgProperty, v);
        public static Brush? GetItemSelectedForeground(DependencyObject o) => (Brush?)o.GetValue(ItemSelectedForegroundProperty);
        public static void SetItemSelectedForeground(DependencyObject o, Brush? v) => o.SetValue(ItemSelectedForegroundProperty, v);
        public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
        public Brush? PlaceholderForeground { get => (Brush?)GetValue(PlaceholderForegroundProperty); set => SetValue(PlaceholderForegroundProperty, value); }
        public Brush? OnHoverBg { get => (Brush?)GetValue(OnHoverBgProperty); set => SetValue(OnHoverBgProperty, value); }
        public Brush? OnHoverBorder { get => (Brush?)GetValue(OnHoverBorderProperty); set => SetValue(OnHoverBorderProperty, value); }
        public Brush? OnHoverForeground { get => (Brush?)GetValue(OnHoverForegroundProperty); set => SetValue(OnHoverForegroundProperty, value); }
        public Brush? DropDownBg { get => (Brush?)GetValue(DropDownBgProperty); set => SetValue(DropDownBgProperty, value); }
        public Brush? DropDownBorder { get => (Brush?)GetValue(DropDownBorderProperty); set => SetValue(DropDownBorderProperty, value); }
        public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
        public bool IsSearchEnabled { get => (bool)GetValue(IsSearchEnabledProperty); set => SetValue(IsSearchEnabledProperty, value); }
        public string SearchPlaceholder { get => (string)GetValue(SearchPlaceholderProperty); set => SetValue(SearchPlaceholderProperty, value); }
        public string NoResultsText { get => (string)GetValue(NoResultsTextProperty); set => SetValue(NoResultsTextProperty, value); }
        public string? SearchMemberPath { get => (string?)GetValue(SearchMemberPathProperty); set => SetValue(SearchMemberPathProperty, value); }
        public Brush? ItemHoverBg { get => GetItemHoverBg(this); set => SetItemHoverBg(this, value); }
        public Brush? ItemHoverForeground { get => GetItemHoverForeground(this); set => SetItemHoverForeground(this, value); }
        public Brush? ItemSelectedBg { get => GetItemSelectedBg(this); set => SetItemSelectedBg(this, value); }
        public Brush? ItemSelectedForeground { get => GetItemSelectedForeground(this); set => SetItemSelectedForeground(this, value); }
        private TextBox? _search, _edit;
        private TextBlock? _hint, _noResults;
        private DispatcherTimer? _debounce;
        private bool _filtering, _picked;
        private object? _preOpen;
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            Detach();
            _search = GetTemplateChild("PART_SearchBox") as TextBox;
            _edit = GetTemplateChild("PART_EditableTextBox") as TextBox;
            _hint = GetTemplateChild("PART_SearchHint") as TextBlock;
            _noResults = GetTemplateChild("PART_NoResults") as TextBlock;
            if (_search != null)
            {
                _search.TextChanged += OnSearchChanged;
                _search.PreviewKeyDown += OnSearchKey;
                _search.KeyDown += OnSearchSpace;
            }
            if (_edit != null) _edit.TextChanged += OnEditTextChanged;
        }
        private void Detach()
        {
            if (_search != null)
            {
                _search.TextChanged -= OnSearchChanged;
                _search.PreviewKeyDown -= OnSearchKey;
                _search.KeyDown -= OnSearchSpace;
            }
            if (_edit != null) _edit.TextChanged -= OnEditTextChanged;
        }
        protected override void OnDropDownOpened(EventArgs e)
        {
            base.OnDropDownOpened(e);
            if (IsReadOnly) { IsDropDownOpen = false; return; }
            _preOpen = SelectedItem;
            _picked = false;
            if (!IsEditable && IsSearchEnabled && _search != null)
            {
                _search.Text = "";
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    _search.Focus();
                    Keyboard.Focus(_search);
                }));
            }
        }
        protected override void OnDropDownClosed(EventArgs e)
        {
            base.OnDropDownClosed(e);
            _debounce?.Stop();
            if (_search != null && _search.Text.Length > 0) _search.Text = "";
            _debounce?.Stop();
            ClearFilter();
            if (!IsEditable && !_picked && SelectedItem == null && _preOpen != null)
                SelectedItem = _preOpen;
            _preOpen = null;
        }
        protected override void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            base.OnSelectionChanged(e);
            if (IsDropDownOpen && !_filtering && SelectedItem != null) _picked = true;
        }
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (IsReadOnly)
            {
                bool block = e.Key switch
                {
                    Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.F4 => true,
                    Key.Space or Key.Home or Key.End => !IsEditable,
                    Key.System => e.SystemKey is Key.Down or Key.Up or Key.F4,
                    _ => false
                };
                if (block) { e.Handled = true; return; }
            }
            base.OnPreviewKeyDown(e);
        }
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if (IsReadOnly) return;   
            base.OnMouseWheel(e);
        }
        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (_hint != null)
                _hint.Visibility = string.IsNullOrEmpty(_search!.Text) ? Visibility.Visible : Visibility.Collapsed;
            _debounce ??= CreateDebounce();
            _debounce.Stop();
            _debounce.Start();
        }
        private DispatcherTimer CreateDebounce()
        {
            var t = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(100) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (_search != null) ApplyFilter(_search.Text);
            };
            return t;
        }
        private void OnSearchKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    _debounce?.Stop();
                    if (_search != null) ApplyFilter(_search.Text);   
                    if (SelectedIndex < 0 && Items.Count > 0) SelectedIndex = 0;
                    IsDropDownOpen = false;
                    e.Handled = true;
                    break;
                case Key.Escape:
                    IsDropDownOpen = false;
                    e.Handled = true;
                    break;
                case Key.Down:
                case Key.Up:
                    if (Items.Count > 0)
                    {
                        _debounce?.Stop();
                        if (_search != null) ApplyFilter(_search.Text);
                        int i = Math.Clamp(SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Items.Count - 1);
                        SelectedIndex = i;
                        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                            (ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement)?.BringIntoView()));
                    }
                    e.Handled = true;
                    break;
            }
        }
        private static void OnSearchSpace(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space) e.Handled = true;
        }
        private void OnEditTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_filtering || !IsEditable || IsReadOnly || _edit == null || !_edit.IsKeyboardFocused) return;
            string text = _edit.Text;
            if (SelectedItem != null &&
                string.Equals(GetText(SelectedItem), text, StringComparison.OrdinalIgnoreCase)) return;
            int caret = _edit.CaretIndex;
            ApplyFilter(text);
            if (_edit.Text != text)
            {
                _filtering = true;
                _edit.Text = text;
                _edit.CaretIndex = caret;
                _filtering = false;
            }
            if (!IsDropDownOpen)
            {
                IsDropDownOpen = true;
                _edit.SelectionStart = caret;
                _edit.SelectionLength = 0;
            }
        }
        private void ApplyFilter(string? text)
        {
            if (_filtering) return;
            _filtering = true;
            try
            {
                string q = text?.Trim() ?? "";
                if (q.Length == 0)
                {
                    if (Items.Filter != null) Items.Filter = null;
                    SetNoResults(false);
                }
                else
                {
                    Items.Filter = o => Matches(o, q);
                    SetNoResults(Items.Count == 0);
                }
            }
            catch (NotSupportedException) {  }
            finally { _filtering = false; }
        }
        private void ClearFilter()
        {
            _filtering = true;
            try { if (Items.Filter != null) Items.Filter = null; }
            catch (NotSupportedException) { }
            finally { _filtering = false; }
            SetNoResults(false);
        }
        private void SetNoResults(bool on)
        {
            if (_noResults != null) _noResults.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }
        private bool Matches(object o, string q)
            => GetText(o).Contains(q, StringComparison.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> PropCache = new();
        private string GetText(object? item)
        {
            if (item == null) return "";
            if (item is string s) return s;
            if (item is ComboBoxItem ci) return ci.Content?.ToString() ?? "";
            string? path = !string.IsNullOrEmpty(SearchMemberPath) ? SearchMemberPath : DisplayMemberPath;
            if (string.IsNullOrEmpty(path)) return item.ToString() ?? "";
            object? cur = item;
            foreach (var part in path.Split('.'))
            {
                if (cur == null) return "";
                var pi = PropCache.GetOrAdd((cur.GetType(), part), k => k.Item1.GetProperty(k.Item2));
                cur = pi?.GetValue(cur);
            }
            return cur?.ToString() ?? "";
        }
    }
}