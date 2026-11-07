using System.Windows;
using System.Windows.Input;
using AssetsHandler.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace YHG_Assets_Handler.Views.Windows
{
    public partial class InputDialogViewModel : ObservableObject
    {
        [ObservableProperty] private string dialogTitle = "Input";
        [ObservableProperty] private string message = "";
        [ObservableProperty] private string placeholder = "";
        [ObservableProperty] private string inputText = "";

        [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))]
        private string errorMessage = "";

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        // Typing shuru karte hi purana error hat jaye
        partial void OnInputTextChanged(string value) => ErrorMessage = "";
    }

    public partial class InputDialog : DarkWindow
    {
        private readonly InputDialogViewModel _vm = new();
        private readonly Func<string, string?>? _validator;

        /// <summary>OK dabane par jo text mila (sirf DialogResult=true par).</summary>
        public string Result => _vm.InputText.Trim();

        /// <param name="validator">null return = theek, string return = error message (dialog band nahi hota)</param>
        public InputDialog(string title, string message, string defaultText = "",
                           string placeholder = "", Func<string, string?>? validator = null)
        {
            InitializeComponent();
            _validator = validator;

            _vm.DialogTitle = title;
            _vm.Message = message;
            _vm.InputText = defaultText;
            _vm.Placeholder = placeholder;
            DataContext = _vm;

            Loaded += (_, _) =>
            {
                InputTextBox.Focus();
                InputTextBox.SelectAll();
            };
        }

        public InputDialog() : this("Input", "") { }

        /// <summary>Ek line me: string? name = InputDialog.Ask(owner, "Rename", "New name:", old);</summary>
        public static string? Ask(Window? owner, string title, string message, string defaultText = "",
                                  string placeholder = "", Func<string, string?>? validator = null)
        {
            var d = new InputDialog(title, message, defaultText, placeholder, validator)
            {
                Owner = owner ?? Application.Current.MainWindow
            };
            return d.ShowDialog() == true ? d.Result : null;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string text = _vm.InputText.Trim();
            string? error = _validator?.Invoke(text);

            if (error != null)
            {
                _vm.ErrorMessage = error;   // dialog khula rahega
                InputTextBox.Focus();
                return;
            }
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        // Enter = OK, Esc = Cancel (DarkButton_01 me IsDefault/IsCancel chalta nahi)
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Enter) { OkButton_Click(this, e); e.Handled = true; }
            else if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        }
    }
}