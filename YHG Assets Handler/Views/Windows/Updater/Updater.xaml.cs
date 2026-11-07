using AssetsHandler.Controls;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;
using YHG_Assets_Handler.Utils;

namespace YHG_Assets_Handler.Views.Windows.Updater
{
    public partial class Updater : DarkWindow
    {
        private UpdateHelper helper = new UpdateHelper();
        private string currentVer = "";
        private UpdateCheckResult? lastCheckResult;
        private string downloadedFilePath = "";

        private enum UpdateState { Idle, ReadyToDownload, Downloading, Downloaded }
        private UpdateState currentState = UpdateState.Idle;

        public Updater()
        {
            InitializeComponent();

            currentVer = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            lblVersionInfo.Text = $"Current: v{currentVer}";

            UpdateHelper.VerifyUpdateLifecycle(currentVer);

            if (UpdateHelper.UpdateStatus == "UpdateAvailable" && !string.IsNullOrEmpty(UpdateHelper.AvailableVersion))
            {
                lblStatus.Text = $"NEW REVISION AVAILABLE: v{UpdateHelper.AvailableVersion}";
                lblStatus.Foreground = Brushes.LimeGreen;
                rtbWhatsNew.Text = UpdateHelper.AvailableChangelog;
                lblWhatsNewTitle.Text = "> NEW VERSION CHANGELOG / WHAT'S NEW:";
                lblWhatsNewTitle.Visibility = Visibility.Visible;
                rtbWhatsNew.Visibility = Visibility.Visible;

                btnAction.Content = "Select & Download";
                btnAction.Background = new SolidColorBrush(Color.FromRgb(200, 40, 120));
                btnAction.OnHoverBg = new SolidColorBrush(Color.FromRgb(220, 60, 140));
                currentState = UpdateState.ReadyToDownload;
            }
            else
            {
                if (!string.IsNullOrEmpty(UpdateHelper.PreviousChangelog))
                {
                    rtbWhatsNew.Text = UpdateHelper.PreviousChangelog;
                    lblWhatsNewTitle.Text = "> PREVIOUS / INSTALLED VERSION CHANGELOG:";
                    lblWhatsNewTitle.Visibility = Visibility.Visible;
                    rtbWhatsNew.Visibility = Visibility.Visible;
                }
                else
                {
                    rtbWhatsNew.Text = "No previous release logs found in local cache. Click 'Check for Update' to query server status.";
                    lblWhatsNewTitle.Text = "> SYSTEM LOG:";
                    lblWhatsNewTitle.Visibility = Visibility.Visible;
                    rtbWhatsNew.Visibility = Visibility.Visible;
                }

                btnAction.Content = "Check for Update";
                currentState = UpdateState.Idle;
            }
        }

        private async void btnAction_Click(object sender, RoutedEventArgs e)
        {
            if (currentState == UpdateState.Downloaded)
            {
                if (!string.IsNullOrEmpty(downloadedFilePath) && File.Exists(downloadedFilePath))
                {
                    FileUtils.OpenInExplorer(downloadedFilePath);
                }
                return;
            }

            if (currentState == UpdateState.Idle)
            {
                btnAction.IsEnabled = false;
                lblStatus.Text = "SYSTEM: Querying GitHub repository...";

                lastCheckResult = await helper.IsUpdateAvailableAsync(currentVer);

                if (lastCheckResult.IsNew)
                {
                    lblStatus.Text = $"NEW REVISION DETECTED: {lastCheckResult.Version}";
                    lblStatus.Foreground = Brushes.LimeGreen;

                    UpdateHelper.SaveSettings(
                        UpdateHelper.PreviousVersion,
                        UpdateHelper.PreviousChangelog,
                        "UpdateAvailable",
                        "",
                        lastCheckResult.Version,
                        lastCheckResult.Changelog
                    );

                    rtbWhatsNew.Text = lastCheckResult.Changelog;
                    lblWhatsNewTitle.Text = "> NEW VERSION CHANGELOG / WHAT'S NEW:";
                    lblWhatsNewTitle.Visibility = Visibility.Visible;
                    rtbWhatsNew.Visibility = Visibility.Visible;

                    // Set asset info & sizes
                    if (lastCheckResult.StandalonePackage != null)
                    {
                        txtStandaloneInfo.Text = $"Standalone (Self-contained) - Size: {lastCheckResult.StandalonePackage.FormattedSize}";
                        rbStandalone.IsEnabled = true;
                    }
                    else
                    {
                        txtStandaloneInfo.Text = "Standalone - Not found in release";
                        rbStandalone.IsEnabled = false;
                    }

                    if (lastCheckResult.LightweightPackage != null)
                    {
                        txtLightweightInfo.Text = $"Lightweight (Requires .NET) - Size: {lastCheckResult.LightweightPackage.FormattedSize}";
                        rbLightweight.IsEnabled = true;
                    }
                    else
                    {
                        txtLightweightInfo.Text = "Lightweight - Not found in release";
                        rbLightweight.IsEnabled = false;
                    }

                    panelPackageSelection.Visibility = Visibility.Visible;

                    btnAction.Content = "Download Update";
                    btnAction.Background = new SolidColorBrush(Color.FromRgb(200, 40, 120));
                    btnAction.OnHoverBg = new SolidColorBrush(Color.FromRgb(220, 60, 140));
                    btnAction.IsEnabled = true;
                    currentState = UpdateState.ReadyToDownload;
                }
                else
                {
                    btnAction.IsEnabled = true;
                    lblStatus.Text = "SYSTEM: You are already running the latest build.";
                    lblStatus.Foreground = Brushes.Cyan;
                    rtbWhatsNew.Text = lastCheckResult.Changelog;
                    lblWhatsNewTitle.Text = "> CURRENT VERSION CHANGELOG:";
                    lblWhatsNewTitle.Visibility = Visibility.Visible;
                    rtbWhatsNew.Visibility = Visibility.Visible;
                }
            }
            else if (currentState == UpdateState.ReadyToDownload)
            {
                if (lastCheckResult == null)
                {
                    lastCheckResult = await helper.IsUpdateAvailableAsync(currentVer);
                }

                // Determine selected asset package
                ReleasePackage? selectedPkg = (rbStandalone.IsChecked == true)
                    ? lastCheckResult.StandalonePackage
                    : lastCheckResult.LightweightPackage;

                // Fallback agar specific package resolve na hua ho
                if (selectedPkg == null)
                {
                    selectedPkg = lastCheckResult.StandalonePackage ?? lastCheckResult.LightweightPackage;
                }

                if (selectedPkg == null || string.IsNullOrEmpty(selectedPkg.DownloadUrl))
                {
                    lblStatus.Text = "ERROR: Failed to resolve download link for selected package.";
                    lblStatus.Foreground = Brushes.Red;
                    btnAction.IsEnabled = true;
                    return;
                }

                var folderDialog = new OpenFolderDialog();
                if (folderDialog.ShowDialog() == true)
                {
                    string fileName = selectedPkg.Name;
                    if (string.IsNullOrEmpty(fileName)) fileName = "YHG_Assets_Handler_Update.zip";
                    string savePath = Path.Combine(folderDialog.FolderName, fileName);

                    btnAction.IsEnabled = false;
                    btnAction.Content = "Downloading...";
                    panelPackageSelection.IsEnabled = false;
                    progressBar1.Value = 0;
                    progressBar1.Visibility = Visibility.Visible;
                    currentState = UpdateState.Downloading;

                    bool success = await helper.DownloadUpdateAsync(selectedPkg.DownloadUrl, savePath, (progress) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            progressBar1.Value = progress;
                            lblStatus.Text = $"DOWNLOADING {selectedPkg.FormattedSize}: {progress}%";
                        });
                    });

                    if (success)
                    {
                        downloadedFilePath = savePath;
                        lblStatus.Text = "STATUS: Download completed successfully.";
                        lblStatus.Foreground = Brushes.LimeGreen;

                        UpdateHelper.SaveSettings(
                            currentVer,
                            UpdateHelper.PreviousChangelog,
                            "DownloadedButNotInstalled",
                            savePath,
                            lastCheckResult.Version,
                            lastCheckResult.Changelog
                        );

                        var dr = ConfirmationDialog.Show(
                            title: "YHG Update System",
                            message: "Download successful!\n\nPackage safely saved. Do you want to close the app now to replace the files?",
                            this);

                        if (dr == true)
                        {
                            Application.Current.Shutdown();
                        }
                        else
                        {
                            btnAction.Content = "Open Download Folder";
                            btnAction.IsEnabled = true;
                            currentState = UpdateState.Downloaded;

                            NotificationManager.Success(
                                title: "Download successful!",
                                message: "Package safely saved. Click the button anytime to open the folder.",
                                onClick: () => FileUtils.OpenInExplorer(savePath));
                        }
                    }
                    else
                    {
                        lblStatus.Text = "ERROR: Connection dropped or write restriction.";
                        lblStatus.Foreground = Brushes.Red;
                        btnAction.Content = "Retry Download";
                        btnAction.IsEnabled = true;
                        panelPackageSelection.IsEnabled = true;
                        currentState = UpdateState.ReadyToDownload;
                    }
                }
            }
        }
    }
}