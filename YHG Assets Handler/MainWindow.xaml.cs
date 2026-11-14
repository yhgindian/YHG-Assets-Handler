using AssetsHandler.Controls;
using AssetsHandler.Core.ProcessManager;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Models.Host;
using YHG_Assets_Handler.ViewModels;
using YHG_Assets_Handler.Views.Windows;
using YHG_Assets_Handler.Views.Windows.Updater;
using YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Texture2D;

namespace YHG_Assets_Handler
{
    public partial class MainWindow : DarkWindow
    {
        private readonly DoubleAnimation _spin = new(0, 360, TimeSpan.FromSeconds(1))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        private bool IsConsoleOpen => TerminalContainerRow.Height.Value > 0;
        private bool _isClosingConfirmed = false;
        private bool _isCheckingCanClose = false;
        public MainWindow()
        {
            InitializeComponent();
            Closing += MainWindow_Closing;
            Status.Default = new StatusConsole(LogRichTextBox)
            {
                PromptSound = ConsoleSound.Asterisk,
            };
            ProcessManager.Host = new ProcessHost(this);
            ProcessManager.ProcessFinished += ProcessManager_ProcessFinished;
            ProcessManager.Processes.CollectionChanged += (_, _) => RefreshTaskItem();
            ProcessManager.ActiveChanged += RefreshReady;
            NotificationManager.Host = new NotificationHost(Dispatcher);
            NotificationManager.Shown += n => Status.Write($"{n.Title}: {n.Message}", n.Type switch
            {
                NotificationType.Success => StatusType.Success,
                NotificationType.Warning => StatusType.Warning,
                NotificationType.Error => StatusType.Error,
                _ => StatusType.Info
            });
            NotificationManager.ShowFilter = n =>
            {
                if (!IsConsoleOpen) return true;
                if (n.Type == NotificationType.Error) return true;
                if (n.DurationMs <= 0) return true;
                if (n.HasClickAction) return true;
                return false;
            };
            this.DataContext = new ViewModels.MainViewModel();

            this.Loaded += MainWindow_Loaded;

        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await UpdateCheckerAsync();
        }

        private async Task UpdateCheckerAsync()
        {
            var result = await UpdateHelper.IsUpdateAvailableAsync();

            if (result.IsNew)
            {
                NotificationManager.Warning(
                    message: "A newer version is available. Please download it.",
                    durationMs: 0,
                    onClick: () =>
                    {
                        Updater updater = new();
                        updater.ShowDialog();
                    });
            }
        }

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_isClosingConfirmed)
                return;
            e.Cancel = true;
            if (ProcessManager.ActiveCount > 0)
            {
                NotificationManager.Warning("Please wait for all background tasks to finish or cancel them first.", "Tasks Running");
                return;
            }
            if (_isCheckingCanClose)
                return;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                try
                {
                    _isCheckingCanClose = true;
                    bool canClose = await vm.CanCloseAppAsync();
                    if (canClose)
                    {
                        vm.OnAppClosing();
                        _isClosingConfirmed = true;
                        _ = Dispatcher.InvokeAsync(Close, System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
                finally
                {
                    _isCheckingCanClose = false;
                }
            }
        }

#if DEBUG
        private void TestOptionSelector()
        {
            var testParams = new List<OptionParam>
    {
        new OptionParam("compression", "Repack Compression", OptionKind.Choice,
            defaultValue: "LZ4",
            description: "Target compression algorithm for data blocks packaging.")
        {
            Choices = new object[] { "LZ4", "LZ4Fast", "LZMA", "None" }
        },
        new OptionParam("blockDirAtEnd", "Pack Directory Info at File End", OptionKind.Bool,
            defaultValue: true,
            description: "Standard layout for UnityFS streaming engines."),
        new OptionParam("stripMipmaps", "Strip Textures Mipmaps", OptionKind.Bool,
            defaultValue: false,
            description: "Removes extra LOD levels to reduce output size."),
        new OptionParam("bundleTag", "Custom Bundle Signature", OptionKind.Text,
            defaultValue: "CAB-MODDED",
            description: "Identifier prefix injected into asset metadata.")
        {
            Validator = val =>
            {
                string s = val?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(s)) return "Tag cannot be blank.";
                if (s.Contains(' ')) return "Tag cannot contain spaces.";
                return null;
            }
        },
        new OptionParam("chunkSizeKb", "Block Chunk Size (KB)", OptionKind.Number,
            defaultValue: "128",
            description: "Memory streaming granularity. Recommended standard is 128.")
        {
            Validator = val =>
            {
                if (val is double d)
                {
                    if (d < 64 || d > 1024) return "Chunk size must be between 64 and 1024 KB.";
                    return null;
                }
                return "Enter a valid integer number.";
            }
        }
    };
            var results = OptionSelectorWindow.Ask(
                this,
                "OptionSelector Diagnostic Test",
                testParams,
                "Testing all OptionKind variants (Choice, Bool, Text, Number) with live validation:"
            );
            if (results == null)
            {
                Status.Write("[OptionSelector Test] User clicked Cancel or pressed Escape.", StatusType.Warning);
                return;
            }
            Status.Write("[OptionSelector Test] Captured Config Results:", StatusType.Success);
            foreach (var (key, value) in results)
            {
                string typeName = value?.GetType().Name ?? "null";
                Status.Write($"  • [{key}] => \"{value}\" (Type: {typeName})", StatusType.Info);
            }
        }
        private async Task TestNotifications()
        {
            void Log(string s) => Debug.WriteLine($"[NOTIF TEST] {DateTime.Now:HH:mm:ss.fff} {s}");
            await Task.Delay(1000);
            Log("1: Success (4s, auto-dismiss)");
            NotificationManager.Success("User successfully blocked!");
            await Task.Delay(1500);
            Log("2: Info");
            NotificationManager.Info("Scan started");
            await Task.Delay(1500);
            Log("3: Warning");
            NotificationManager.Warning("Cache folder missing");
            await Task.Delay(1500);
            Log("4: Error sticky (durationMs: 0)");
            NotificationManager.Error("Server unreachable", title: "Connection lost", durationMs: 0);
            await Task.Delay(1500);
            Log("5: key replace x3");
            for (int i = 0; i < 3; i++)
            {
                NotificationManager.Info($"Saved #{i}", key: "autosave");
                await Task.Delay(300);
            }
            await Task.Delay(1500);
            Log("6: flood 10 (MaxVisible trim)");
            for (int i = 0; i < 10; i++)
                NotificationManager.Info($"Flood {i}");
            await Task.Delay(2000);
            Log("7: from background thread");
            await Task.Run(() => NotificationManager.Warning("From background"));
            await Task.Delay(1500);
            Log("8: DismissAll");
            NotificationManager.DismissAll();
            Log("DONE, freeze nahi hua");
        }
        private async Task TestProcesses()
        {
            var t1 = ProcessManager.RunAsync("Importing assets", async h =>
            {
                const int total = 50;
                for (int i = 1; i <= total; i++)
                {
                    h.Token.ThrowIfCancellationRequested();
                    await Task.Delay(150, h.Token);
                    h.Report(i * 100.0 / total, $"{i}/{total}  asset_{i}.png");
                }
            });
            var t2 = ProcessManager.RunAsync("Connecting to server", async h =>
            {
                h.SetIndeterminate("Contacting server...");
                await Task.Delay(3000, h.Token);
                h.SetStatus("Handshake done");
                await Task.Delay(1500, h.Token);
            });
            var t3 = ProcessManager.RunBackground("Hashing files", h =>
            {
                for (int i = 1; i <= 100; i++)
                {
                    if (h.Token.IsCancellationRequested)
                    {
                        Thread.Sleep(60);
                        h.Report(i, $"hashing {i}/100");
                    }
                }
            });
            var t4 = ProcessManager.RunAsync("Downloading", h => FakeDownloadAsync(h.Progress, h.Token));
            var t5 = ProcessManager.RunAsync("Exporting (will fail)", async h =>
            {
                h.Report(30, "Writing file...");
                await Task.Delay(2000, h.Token);
                throw new IOException("Disk full");
            });
            var t6 = ProcessManager.RunAsync("Saving project", async h =>
            {
                for (int i = 1; i <= 10; i++)
                {
                    await Task.Delay(300);
                    h.Report(i * 10);
                }
            }, canCancel: false);
            bool[] results = await Task.WhenAll(t1, t2, t3, t4, t5, t6);
            Status.Write($"{results.Count(r => r)}/{results.Length} processes succeeded");
            var p = ProcessManager.Start("Manual export", canCancel: true);
            try
            {
                for (int i = 1; i <= 20; i++)
                {
                    if (p.IsCancellationRequested) { p.Cancelled("Stopped by user"); return; }
                    await Task.Delay(200);
                    p.Report(i * 5, $"chunk {i}/20");
                }
                p.Complete("Exported");
            }
            catch (Exception ex) { p.Fail(ex.Message); }
        }
        private static async Task FakeDownloadAsync(IProgress<double> progress, CancellationToken ct)
        {
            for (int i = 0; i <= 100; i += 2)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(i);
                await Task.Delay(80, ct);
            }
        }

#endif


        private void ProcessManager_ProcessFinished(ProcessItem obj)
        {
            RefreshTaskItem();
            if (obj.State == ProcessState.Failed)
                NotificationManager.Error(obj.Status, obj.Title, durationMs: 0);
            else
                Status.Write($"{obj.Title}: {obj.Status}",
                    obj.State == ProcessState.Completed ? StatusType.Success : StatusType.Warning);
        }
        private void RefreshTaskItem()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(RefreshTaskItem); return; }
            var current = ProcessManager.Processes.LastOrDefault(p => !p.IsFinished);
            TaskItem.DataContext = current;
            TaskItem.Visibility = current != null ? Visibility.Visible : Visibility.Collapsed;
        }
        private void RefreshReady()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(RefreshReady); return; }
            int n = ProcessManager.ActiveCount;
            bool busy = n > 0;
            ReadyIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            BusyIcon.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ReadyText.Text = !busy ? "Ready" : n == 1 ? "Working..." : $"Working... ({n})";
            BusySpin.BeginAnimation(RotateTransform.AngleProperty, busy ? _spin : null);
        }
        private void ToggleStatusBoxBtn_Click(object sender, RoutedEventArgs e)
        {
            if (TerminalContainerRow.Height.Value == 0)
            {
                TerminalSplitterRow.MinHeight = UIConstants.SplitterSize;
                TerminalSplitterRow.Height = new GridLength(UIConstants.SplitterSize);
                TerminalContainerRow.MinHeight = UIConstants.TerminalMinHeight;
                TerminalContainerRow.Height = new GridLength(UIConstants.TerminalDefaultHeight);
                TerminalSplitter.Visibility = Visibility.Visible;
            }
            else
            {
                TerminalContainerRow.MinHeight = 0;
                TerminalContainerRow.Height = new GridLength(0);
                TerminalSplitterRow.MinHeight = 0;
                TerminalSplitterRow.Height = new GridLength(0);
                TerminalSplitter.Visibility = Visibility.Collapsed;
            }
        }
        private void DarkProgressBar_01_ProgressClick(object sender, ProgressClickEventArgs e)
        {
            ProcessManager.Show();
        }
        private void solution_ex_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                if (e.NewValue is Models.ExplorerItem selectedItem)
                {
                    vm.OnItemSelected(selectedItem);
                }
            }
        }
        private void solution_ex_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void solution_ex_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    foreach (string filePath in files)
                    {
                        if (DataContext is ViewModels.MainViewModel vm)
                        {
                            vm.LoadFileDirectly(filePath);
                        }
                    }
                }
            }
        }
    }
}