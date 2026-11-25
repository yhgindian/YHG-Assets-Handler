using AssetsHandler.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace YHG_Assets_Handler.Views.Windows
{
    /// <summary>
    /// Interaction logic for About.xaml
    /// </summary>
    public partial class About : DarkWindow
    {
        public About()
        {
            InitializeComponent();

            var loaded = YHG_Assets_Handler.Plugins.PluginManager.Instance.Results
                .Where(r => r.Status == YHG_Assets_Handler.Plugins.PluginStatus.Loaded && r.Info != null)
                .Select(r => r.Info!)
                .Select(i => new
                {
                    Title = $"{i.Name}  v{i.Version}",
                    Details = string.Join("  ·  ", new[]
                    {
                        i.Author,
                        string.IsNullOrWhiteSpace(i.License) ? "" : "License: " + i.License,
                        string.IsNullOrWhiteSpace(i.Credits) ? "" : "Credits: " + i.Credits
                    }.Where(x => !string.IsNullOrWhiteSpace(x)))
                })
                .ToList();

            PluginList.ItemsSource = loaded;
            NoPluginsText.Visibility = loaded.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenLink_RequestNavigate(
            object sender,
            RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to open the link.\n\n{ex.Message}",
                    "YHG Assets Handler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            e.Handled = true;
        }
    }
}
