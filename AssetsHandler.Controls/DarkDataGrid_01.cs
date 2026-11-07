using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AssetsHandler.Controls
{
    public class DarkDataGrid_01 : DataGrid, IFindTarget
    {
        static DarkDataGrid_01()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(DarkDataGrid_01),
                new FrameworkPropertyMetadata(typeof(DarkDataGrid_01)));
        }
        public static readonly DependencyProperty SelectionDelayMsProperty = DependencyProperty.Register(
            nameof(SelectionDelayMs), typeof(int), typeof(DarkDataGrid_01), new PropertyMetadata(60));
        public static readonly DependencyProperty SelectionSettledCommandProperty = DependencyProperty.Register(
            nameof(SelectionSettledCommand), typeof(ICommand), typeof(DarkDataGrid_01), new PropertyMetadata(null));
        public int SelectionDelayMs
        {
            get => (int)GetValue(SelectionDelayMsProperty);
            set => SetValue(SelectionDelayMsProperty, value);
        }
        public ICommand? SelectionSettledCommand
        {
            get => (ICommand?)GetValue(SelectionSettledCommandProperty);
            set => SetValue(SelectionSettledCommandProperty, value);
        }
        public static readonly RoutedEvent SelectionSettledEvent = EventManager.RegisterRoutedEvent(
            nameof(SelectionSettled), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DarkDataGrid_01));
        public event RoutedEventHandler SelectionSettled
        {
            add => AddHandler(SelectionSettledEvent, value);
            remove => RemoveHandler(SelectionSettledEvent, value);
        }
        private DispatcherTimer? _timer;
        protected override void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            base.OnSelectionChanged(e);
            _timer ??= new DispatcherTimer(DispatcherPriority.Input);
            _timer.Stop();
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, SelectionDelayMs));
            _timer.Tick -= OnSettled;
            _timer.Tick += OnSettled;
            _timer.Start();
        }
        private void OnSettled(object? s, EventArgs e)
        {
            _timer?.Stop();
            RaiseEvent(new RoutedEventArgs(SelectionSettledEvent, this));
            if (SelectionSettledCommand?.CanExecute(SelectedItems) == true)
                SelectionSettledCommand.Execute(SelectedItems);
        }
        public static readonly DependencyProperty SearchColumnsProperty = DependencyProperty.Register(
            nameof(SearchColumns), typeof(string), typeof(DarkDataGrid_01), new PropertyMetadata(string.Empty));
        public string SearchColumns
        {
            get => (string)GetValue(SearchColumnsProperty);
            set => SetValue(SearchColumnsProperty, value);
        }
        private FindWindow? _findWindow;
        private readonly List<int> _matchedIndices = new();
        private int _currentMatchPointer = -1;
        private string _lastQueryKey = string.Empty;
        public DarkDataGrid_01()
        {
            IsVisibleChanged += (s, e) =>
            {
                if (e.NewValue is bool isVis && !isVis)
                    HideFindWindow();
            };
            Unloaded += (s, e) => HideFindWindow();
            DataContextChanged += (s, e) =>
            {
                HideFindWindow();
                OnFindClosed();
            };
        }
        protected override void OnItemsSourceChanged(IEnumerable oldValue, IEnumerable newValue)
        {
            base.OnItemsSourceChanged(oldValue, newValue);
            HideFindWindow();
            OnFindClosed();
        }
        public void HideFindWindow()
        {
            if (_findWindow != null && _findWindow.IsVisible)
            {
                _findWindow.Hide();
            }
        }
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            var actualKey = (e.Key == Key.System) ? e.SystemKey : e.Key;
            if (actualKey == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ShowFind();
                e.Handled = true;
            }
        }
        public void ShowFind()
        {
            if (_findWindow == null)
            {
                _findWindow = new FindWindow();
                _findWindow.AttachTarget(this);
            }
            Window parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                _findWindow.Owner = parentWindow;
            }
            _findWindow.Show();
            _findWindow.Activate();
            _findWindow.FocusSearchBox();
        }
        public FindResult Find(FindOptions options)
        {
            if (ItemsSource == null || string.IsNullOrEmpty(options.Query))
            {
                _matchedIndices.Clear();
                _currentMatchPointer = -1;
                return new FindResult(0, 0, false);
            }
            string queryKey = $"{options.Query}|{options.MatchCase}|{options.WholeWord}|{options.UseRegex}|{SearchColumns}";
            bool isNewQuery = _lastQueryKey != queryKey;
            if (isNewQuery)
            {
                BuildMatches(options);
                _lastQueryKey = queryKey;
            }
            if (_matchedIndices.Count == 0)
            {
                return new FindResult(0, 0, false);
            }
            if (isNewQuery)
            {
                _currentMatchPointer = 0;
            }
            else
            {
                if (options.Forward)
                {
                    _currentMatchPointer++;
                    if (_currentMatchPointer >= _matchedIndices.Count)
                        _currentMatchPointer = 0;
                }
                else
                {
                    _currentMatchPointer--;
                    if (_currentMatchPointer < 0)
                        _currentMatchPointer = _matchedIndices.Count - 1;
                }
            }
            int targetIndex = _matchedIndices[_currentMatchPointer];
            if (ItemsSource is IList list && targetIndex >= 0 && targetIndex < list.Count)
            {
                var targetItem = list[targetIndex];
                SelectedItem = targetItem;
                ScrollIntoView(targetItem);
            }
            return new FindResult(_currentMatchPointer + 1, _matchedIndices.Count, true);
        }
        private void BuildMatches(FindOptions options)
        {
            _matchedIndices.Clear();
            _currentMatchPointer = -1;
            Regex regex;
            try
            {
                var regOpts = options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                string pattern = options.UseRegex ? options.Query : Regex.Escape(options.Query);
                if (options.WholeWord)
                    pattern = $@"\b{pattern}\b";
                regex = new Regex(pattern, regOpts | RegexOptions.Compiled);
            }
            catch
            {
                return;
            }
            string[] explicitCols = string.IsNullOrWhiteSpace(SearchColumns)
                ? Array.Empty<string>()
                : SearchColumns.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (ItemsSource is not IList list)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item != null && ItemMatches(item, regex, explicitCols))
                {
                    _matchedIndices.Add(i);
                }
            }
        }
        private static bool ItemMatches(object item, Regex regex, string[] explicitCols)
        {
            PropertyInfo[] props = item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (!prop.CanRead) continue;
                if (explicitCols.Length > 0 &&
                    !Array.Exists(explicitCols, c => c.Equals(prop.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                object? val = prop.GetValue(item);
                if (val != null && regex.IsMatch(val.ToString() ?? string.Empty))
                {
                    return true;
                }
            }
            return false;
        }
        public void OnFindClosed()
        {
            _matchedIndices.Clear();
            _currentMatchPointer = -1;
            _lastQueryKey = string.Empty;
        }
    }
}