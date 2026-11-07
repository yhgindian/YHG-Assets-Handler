using AssetsHandler.Controls;
using System.Windows;
using System.Windows.Input;
using YHG_Assets_Handler.Models;

namespace YHG_Assets_Handler.Views.Windows
{
    public partial class OptionSelectorWindow : DarkWindow
    {
        private readonly IReadOnlyList<OptionParam> _params;

        /// <summary>OK ke baad: key -> value. Number = double, Bool = bool, Text = string, Choice = selected object.</summary>
        public Dictionary<string, object?> Result { get; private set; } = new();

        public OptionSelectorWindow(string title, IReadOnlyList<OptionParam> parameters, string? header = null)
        {
            InitializeComponent();
            _params = parameters;
            Title = title;

            HeaderText.Text = header ?? "";
            HeaderText.Visibility = string.IsNullOrEmpty(header) ? Visibility.Collapsed : Visibility.Visible;
            OptionsList.ItemsSource = parameters;
        }

        public OptionSelectorWindow() : this("Options", Array.Empty<OptionParam>()) { }

        /// <summary>Ek line me. Cancel par null.</summary>
        public static Dictionary<string, object?>? Ask(Window? owner, string title,
            IReadOnlyList<OptionParam> parameters, string? header = null)
        {
            var w = new OptionSelectorWindow(title, parameters, header)
            {
                Owner = owner ?? Application.Current.MainWindow
            };
            return w.ShowDialog() == true ? w.Result : null;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            bool valid = true;
            foreach (var p in _params) valid &= p.Validate();   // saare errors ek saath dikhao
            if (!valid) return;

            Result = _params.ToDictionary(p => p.Key, p => p.GetFinalValue());
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _params) p.Reset();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
            else if (e.Key == Key.Enter && e.OriginalSource is not System.Windows.Controls.ComboBox)
            { Ok_Click(this, e); e.Handled = true; }
        }
    }
}