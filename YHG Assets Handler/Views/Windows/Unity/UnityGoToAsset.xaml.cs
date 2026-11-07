using AssetsHandler.Controls;
using System;
using System.Windows;
using System.Windows.Input;

namespace YHG_Assets_Handler.Views.Windows.Unity
{
    public partial class UnityGoToAsset : DarkWindow
    {
        private readonly Func<int, long, bool>? _assetValidator;

        public int TargetFileId { get; private set; }
        public long TargetPathId { get; private set; }

        // Validator callback pass karo: (fileID, pathID) => returns true/false
        public UnityGoToAsset(Func<int, long, bool>? assetValidator = null, int defaultFileId = 0, long defaultPathId = 0)
        {
            InitializeComponent();
            _assetValidator = assetValidator;

            TxtFileId.Text = defaultFileId.ToString();
            TxtPathId.Text = defaultPathId == 0 ? string.Empty : defaultPathId.ToString();

            Loaded += (s, e) =>
            {
                TxtPathId.Focus();
                TxtPathId.SelectAll();
            };
        }

        private void Go_Click(object sender, RoutedEventArgs e) => ExecuteGo();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ExecuteGo();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
        }

        private void ExecuteGo()
        {
            TxtStatus.Visibility = Visibility.Collapsed;

            // 1. Format check
            if (!int.TryParse(TxtFileId.Text.Trim(), out int fileId))
            {
                ShowError("Invalid File ID format.");
                TxtFileId.Focus();
                return;
            }

            if (!long.TryParse(TxtPathId.Text.Trim(), out long pathId))
            {
                ShowError("Invalid Path ID format.");
                TxtPathId.Focus();
                return;
            }

            // 2. Existence check (Dialog close hone se pehle!)
            if (_assetValidator != null && !_assetValidator(fileId, pathId))
            {
                ShowError($"Asset not found! (FileID: {fileId}, PathID: {pathId})");
                TxtPathId.Focus();
                TxtPathId.SelectAll();
                return;
            }

            TargetFileId = fileId;
            TargetPathId = pathId;

            DialogResult = true;
            Close();
        }

        private void ShowError(string message)
        {
            TxtStatus.Text = message;
            TxtStatus.Visibility = Visibility.Visible;
        }
    }
}