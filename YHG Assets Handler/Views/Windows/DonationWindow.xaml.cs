using System.Diagnostics;
using System.Windows;
using AssetsHandler.Controls;
using AssetsHandler.Core.NotificationManager;

namespace YHG_Assets_Handler.Views.Windows
{
    public partial class DonationWindow : DarkWindow
    {
        private const string CoffeeUrl = "https://buymeacoffee.com/yhgindian";
        private const string UpiId = "yellowhostgamingbs1@oksbi";

        public DonationWindow()
        {
            InitializeComponent();
        }

        private void BuyMeACoffee_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(CoffeeUrl) { UseShellExecute = true });
            }
            catch (System.Exception ex)
            {
                NotificationManager.Error(ex.Message, "Could not open link");
            }
        }

        private void CopyUpi_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(UpiId);
                NotificationManager.Success("UPI ID copied to clipboard", "Copied", durationMs: 2000);
            }
            catch (System.Exception ex)
            {
                NotificationManager.Error(ex.Message, "Copy failed");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}