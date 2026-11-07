using AssetsHandler.Controls;
using System;
using System.Windows;

namespace YHG_Assets_Handler.Views
{
    public partial class ConfirmationDialog : DarkWindow
    {
        public Action<bool>? Allow;

        public ConfirmationDialog()
        {
            InitializeComponent();
        }

        public ConfirmationDialog(string title, string message) : this()
        {
            Title = title;
            TitleBlock.Text = title;
            MessageBlock.Text = message;
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            Allow?.Invoke(true);
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Allow?.Invoke(false);
            DialogResult = false;
            Close();
        }

        public static bool Show(string title, string message, Window? owner = null)
        {
            var dialog = new ConfirmationDialog(title, message)
            {
                Owner = owner ?? Application.Current.MainWindow
            };
            return dialog.ShowDialog() == true;
        }
    }
}