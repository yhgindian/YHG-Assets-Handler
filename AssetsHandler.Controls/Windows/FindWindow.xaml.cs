using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AssetsHandler.Controls
{
    public partial class FindWindow : DarkWindow
    {
        private IFindTarget? _target;
        private readonly DispatcherTimer _typeDebounceTimer;

        public FindWindow()
        {
            InitializeComponent();

            // Typing ke waqt instant snappy response ke liye 80ms ka debounce
            _typeDebounceTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(80)
            };
            _typeDebounceTimer.Tick += (s, e) =>
            {
                _typeDebounceTimer.Stop();
                TriggerSearch(forward: true);
            };

            // Options badalne par turant re-search ho
            BtnMatchCase.Click += (s, e) => TriggerSearch(forward: true);
            BtnWholeWord.Click += (s, e) => TriggerSearch(forward: true);
            BtnRegex.Click += (s, e) => TriggerSearch(forward: true);

            // Text change hote hi bina Enter dabaye search trigger ho
            FindTextBox.TextChanged += FindTextBox_TextChanged;
        }

        public void AttachTarget(IFindTarget target)
        {
            _target = target;
        }

        public void FocusSearchBox()
        {
            FindTextBox.Focus();
            FindTextBox.SelectAll();
        }

        private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _typeDebounceTimer.Stop();
            _typeDebounceTimer.Start();
        }

        private void TriggerSearch(bool forward)
        {
            if (_target == null) return;

            string query = FindTextBox.Text;
            if (string.IsNullOrEmpty(query))
            {
                TxtMatchStatus.Text = "0 results";
                _target.OnFindClosed();
                return;
            }

            var options = new FindOptions(
                Query: query,
                MatchCase: BtnMatchCase.IsChecked == true,
                WholeWord: BtnWholeWord.IsChecked == true,
                UseRegex: BtnRegex.IsChecked == true,
                Forward: forward
            );

            var result = _target.Find(options);

            TxtMatchStatus.Text = result.HasMatches
                ? $"{result.CurrentIndex} of {result.TotalMatches}"
                : "0 results";
        }

        private void NextMatch_Click(object sender, RoutedEventArgs e) => TriggerSearch(forward: true);

        private void PrevMatch_Click(object sender, RoutedEventArgs e) => TriggerSearch(forward: false);

        private void FindTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Enter dabane par agla match (Next/Prev) traverse hoga
                _typeDebounceTimer.Stop();
                bool forward = (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift;
                TriggerSearch(forward);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Dismiss();
                e.Handled = true;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Dismiss();

        public void Dismiss()
        {
            _typeDebounceTimer.Stop();
            _target?.OnFindClosed();
            Hide();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            Dismiss();
        }
    }
}