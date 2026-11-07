using AssetsHandler.Controls;
using Microsoft.Win32;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YHG_Assets_Handler.Utils.Unity;

namespace YHG_Assets_Handler.Views.Windows.Tools
{
    public partial class UnityAssetsMergeSplitter : DarkWindow
    {
        public UnityAssetsMergeSplitter()
        {
            InitializeComponent();
            AttachEvents();
        }

        private void AttachEvents()
        {
            // Split Tab Events
            BrowseSplit.Click += BrowseSplit_Click;
            StartSplit.Click += StartSplit_Click;
            RadioBySize.Checked += (s, e) => { if (LabelNumeric != null) LabelNumeric.Text = "Size (MB)"; };
            RadioByNumber.Checked += (s, e) => { if (LabelNumeric != null) LabelNumeric.Text = "Number of files"; };

            // Merge Tab Events
            BrowseMerge.Click += BrowseMerge_Click;
            StartMerge.Click += StartMerge_Click;
        }

        #region Splitter Logic

        private void BrowseSplit_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "Select File to Split",
                Filter = "All Files (*.*)|*.*"
            };

            if (ofd.ShowDialog() == true)
            {
                TextBoxSplit.Text = ofd.FileName;
                TextBoxOutput.Text = Path.GetFileName(ofd.FileName);
                ToolStripStatusLabel.Text = $"Selected: {Path.GetFileName(ofd.FileName)}";
            }
        }

        private async void StartSplit_Click(object sender, RoutedEventArgs e)
        {
            string inputPath = TextBoxSplit.Text.Trim();
            if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
            {
                MessageBox.Show("Please select a valid source file to split.", "File Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(TextBoxOutput.Text))
            {
                MessageBox.Show("Please specify an output filename.", "Missing Filename", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!long.TryParse(NumericSplitSize.Text?.Trim(), out long splitValue) || splitValue <= 0)
            {
                MessageBox.Show("Please enter a valid numeric value greater than 0.", "Invalid Value", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Target folder picker
            var dialog = new OpenFolderDialog
            {
                Title = "Select Destination Folder For Split Chunks"
            };

            if (dialog.ShowDialog() != true)
                return;

            string targetFolder = dialog.FolderName;
            string outBaseName = TextBoxOutput.Text.Trim();
            bool isSplitBySize = RadioBySize.IsChecked == true;

            SetControlsEnabled(false);
            ToolStripStatusLabel.Text = "Splitting file in progress...";

            try
            {
                await Task.Run(() => PerformSplit(inputPath, targetFolder, outBaseName, splitValue, isSplitBySize));
                ToolStripStatusLabel.Text = "Split completed successfully!";
                MessageBox.Show("File successfully split without data loss!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ToolStripStatusLabel.Text = "Split failed!";
                MessageBox.Show($"Error splitting file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetControlsEnabled(true);
            }
        }

        private void PerformSplit(string inputPath, string targetFolder, string outBaseName, long splitVal, bool splitBySize)
        {
            long totalSize = new FileInfo(inputPath).Length;
            byte[] buffer = new byte[128 * 1024];

            using (FileStream input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (splitBySize)
                {
                    long chunkSize = NearestPowerOfTwoChunk(splitVal * 1024 * 1024);
                    int partIndex = 0;

                    while (input.Position < totalSize)
                    {
                        string chunkPath = Path.Combine(targetFolder, $"{outBaseName}.split{partIndex}");
                        using (FileStream output = new FileStream(chunkPath, FileMode.Create, FileAccess.Write))
                        {
                            long remaining = totalSize - input.Position;
                            long bytesToWrite = Math.Min(chunkSize, remaining);

                            while (bytesToWrite > 0)
                            {
                                int toRead = (int)Math.Min(buffer.Length, bytesToWrite);
                                int read = input.Read(buffer, 0, toRead);
                                if (read <= 0) break;

                                output.Write(buffer, 0, read);
                                bytesToWrite -= read;
                            }
                        }
                        partIndex++;
                    }
                }
                else
                {
                    int parts = (int)splitVal;
                    long suggestedSize = (long)Math.Ceiling((double)totalSize / parts);
                    long chunkSize = NearestPowerOfTwoChunk(suggestedSize);

                    for (int part = 0; part < parts && input.Position < totalSize; part++)
                    {
                        string chunkPath = Path.Combine(targetFolder, $"{outBaseName}.split{part}");
                        using (FileStream output = new FileStream(chunkPath, FileMode.Create, FileAccess.Write))
                        {
                            long remaining = totalSize - input.Position;
                            // Last chunk gets all the leftover bytes
                            long bytesToWrite = (part == parts - 1) ? remaining : Math.Min(chunkSize, remaining);

                            while (bytesToWrite > 0)
                            {
                                int toRead = (int)Math.Min(buffer.Length, bytesToWrite);
                                int read = input.Read(buffer, 0, toRead);
                                if (read <= 0) break;

                                output.Write(buffer, 0, read);
                                bytesToWrite -= read;
                            }
                        }
                    }
                }
            }
        }

        private long NearestPowerOfTwoChunk(long bytes)
        {
            long min = 64 * 1024; // 64 KB minimum chunk
            if (bytes <= min) return min;

            long p = min;
            while (p * 2 <= bytes && p * 2 > 0)
            {
                p *= 2;
            }
            return p;
        }

        #endregion

        #region Merger Logic

        private void BrowseMerge_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "Select any split part (.split*)",
                Filter = "Unity Split Chunks|*.split*|All Files (*.*)|*.*"
            };

            if (ofd.ShowDialog() == true)
            {
                TextBoxMerge.Text = ofd.FileName;
                InitMergeView(ofd.FileName);
            }
        }

        private void InitMergeView(string filePath)
        {
            TreeViewMerge.Items.Clear();
            var parts = AssetsFileMergeHelper.GetOrderedSplitFiles(filePath);

            if (parts.Length == 0)
            {
                ToolStripStatusLabel.Text = "No split chunks detected!";
                return;
            }

            // Root Node
            TreeViewItem rootItem = new TreeViewItem
            {
                Header = new TextBlock
                {
                    Text = $"Detected Chunks ({parts.Length} parts)",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextLight")
                },
                IsExpanded = true
            };

            // Child Nodes
            foreach (var part in parts)
            {
                var fi = new FileInfo(part);
                double sizeInMb = fi.Length / (1024.0 * 1024.0);

                TreeViewItem child = new TreeViewItem
                {
                    Header = new TextBlock
                    {
                        Text = $"{fi.Name} ({sizeInMb:F2} MB)",
                        Foreground = (Brush)FindResource("TextLight")
                    },
                    Tag = part
                };

                rootItem.Items.Add(child);
            }

            TreeViewMerge.Items.Add(rootItem);
            ToolStripStatusLabel.Text = $"Found {parts.Length} split chunks.";
        }

        private async void StartMerge_Click(object sender, RoutedEventArgs e)
        {
            string selected = TextBoxMerge.Text.Trim();
            if (string.IsNullOrEmpty(selected) || !File.Exists(selected))
            {
                MessageBox.Show("Please select a valid split part to merge.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var parts = AssetsFileMergeHelper.GetOrderedSplitFiles(selected);
            if (parts.Length == 0)
            {
                MessageBox.Show("No matching split parts found in folder.", "Missing Chunks", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string defaultName = Path.GetFileName(selected);
            int idx = defaultName.IndexOf(".split", StringComparison.OrdinalIgnoreCase);
            if (idx > 0) defaultName = defaultName.Substring(0, idx);

            SaveFileDialog sfd = new SaveFileDialog
            {
                Title = "Save Merged Output File",
                FileName = defaultName,
                Filter = "All Files (*.*)|*.*"
            };

            if (sfd.ShowDialog() != true)
                return;

            SetControlsEnabled(false);
            ToolStripStatusLabel.Text = "Merging files...";

            try
            {
                var progress = new Progress<double>(pct =>
                {
                    ToolStripStatusLabel.Text = $"Merging: {pct:F0}% completed...";
                });

                await AssetsFileMergeHelper.MergeToFileAsync(selected, sfd.FileName, progress);

                ToolStripStatusLabel.Text = "Merge completed successfully!";
            }
            catch
            {
                ToolStripStatusLabel.Text = "Merge failed!";
            }
            finally
            {
                SetControlsEnabled(true);
            }
        }

        #endregion

        private void SetControlsEnabled(bool enabled)
        {
            StartSplit.IsEnabled = enabled;
            BrowseSplit.IsEnabled = enabled;
            StartMerge.IsEnabled = enabled;
            BrowseMerge.IsEnabled = enabled;
        }
    }
}