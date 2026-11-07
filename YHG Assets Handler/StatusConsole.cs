using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace YHG_Assets_Handler
{
    public class StatusConsole : IStatus
    {
        #region Fields
        private const int NoWrapWidth = 4000;
        private const int MaxOpsPerFlush = 2000;
        private static readonly Brush[] TypeBrushes =
        {
            Frozen("#A4B0BE"), Frozen("#2ECC71"), Frozen("#F1C40F"), Frozen("#E74C3C"), Frozen("#00D2D3")
        };
        private static readonly Brush DimBrush = Frozen("#6B7280");
        private static readonly Brush InputBrush = Frozen("#F1F1F1");
        private static readonly Brush CaretBg = Frozen("#D4D4D4");
        private static readonly Brush CaretFg = Frozen("#1E1E1E");
        private static readonly Brush LinkBrush = Frozen("#00BFFF");
        private readonly RichTextBox _box;
        private readonly Dispatcher _ui;
        private readonly LinkedList<ConsoleLine> _lines = new();
        private readonly ConcurrentQueue<Action> _ops = new();
        private readonly HashSet<StatusType> _hidden = new();
        private int _flushScheduled;
        private volatile ConsoleLine? _lastWritten;
        private bool _stick = true;
        private bool _wrap = true;
        private volatile bool _showTimestamp;
        private readonly SemaphoreSlim _readGate = new(1, 1);
        private readonly StringBuilder _buf = new();
        private readonly List<string> _history = new();
        private readonly DispatcherTimer _blink;
        private TaskCompletionSource<string?>? _readTcs;
        private ConsoleReadOptions? _opts;
        private Paragraph? _inputPara;
        private Run? _rBefore, _rCaret, _rAfter;
        private string _promptText = "";
        private string _draft = "";
        private int _caret, _histPos;
        private bool _caretOn = true;
        #endregion
        #region Public settings
        public bool AutoDetectType { get; set; }
        public bool AutoScroll { get; set; } = true;
        public int MaxLines { get; set; } = 5000;
        public int FlushIntervalMs { get; set; } = 40;
        public ConsoleSound PromptSound { get; set; } = ConsoleSound.Asterisk;
        public ConsoleSound ErrorSound { get; set; } = ConsoleSound.None;
        public Func<string, IEnumerable<string>>? Completer { get; set; }
        public event Action? InputRequested;
        public bool IsReading => _readTcs != null;
        public bool ShowTimestamp
        {
            get => _showTimestamp;
            set { _showTimestamp = value; Enqueue(RebuildDocument); }
        }
        public bool WordWrap
        {
            get => _wrap;
            set
            {
                _wrap = value;
                _box.Document.PageWidth = value ? double.NaN : NoWrapWidth;
                _box.HorizontalScrollBarVisibility = value ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            }
        }
        #endregion
        public StatusConsole(RichTextBox box)
        {
            _box = box ?? throw new ArgumentNullException(nameof(box));
            _ui = _box.Dispatcher;
            _box.IsReadOnly = true;
            _box.IsDocumentEnabled = true;
            _box.IsUndoEnabled = false;
            _box.Document.Blocks.Clear();
            WordWrap = true;
            _box.PreviewKeyDown += OnPreviewKeyDown;
            _box.PreviewTextInput += OnPreviewTextInput;
            _box.PreviewMouseWheel += OnPreviewMouseWheel;
            _box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
            _blink = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromMilliseconds(530) };
            _blink.Tick += (_, _) => { _caretOn = !_caretOn; ApplyCaretVisual(); };
            InitContextMenu();
        }
        public void Detach()
        {
            _blink.Stop();
            _box.PreviewKeyDown -= OnPreviewKeyDown;
            _box.PreviewTextInput -= OnPreviewTextInput;
            _box.PreviewMouseWheel -= OnPreviewMouseWheel;
            _box.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        }
        public IStatusLine Write(string message, StatusType type = StatusType.Info)
        {
            if (string.IsNullOrEmpty(message)) return NullStatusLine.Instance;
            if (AutoDetectType) type = DetectType(message);
            var line = new ConsoleLine(this, message, type);
            _lastWritten = line;
            Enqueue(() => AddLine(line));
            if (type == StatusType.Error) PlaySound(ErrorSound);
            return line;
        }
        public IStatusLine WriteLink(string prefix, string clickablePath, StatusType type = StatusType.Info)
        {
            if (string.IsNullOrWhiteSpace(clickablePath)) return NullStatusLine.Instance;
            if (AutoDetectType) type = DetectType((prefix ?? "") + clickablePath);
            var line = new ConsoleLine(this, prefix ?? "", type, clickablePath);
            _lastWritten = line;
            Enqueue(() => AddLine(line));
            return line;
        }
        public void UpdateLast(string message, StatusType type = StatusType.Info)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (AutoDetectType) type = DetectType(message);
            _lastWritten?.Update(message, type);
        }
        public void UpdateLastColor(StatusType type) => _lastWritten?.SetType(type);
        public void Clear() => Enqueue(ClearNow);
        public void ScrollToEnd()
        {
            _stick = true;
            _ui.BeginInvoke(new Action(() => _box.ScrollToEnd()));
        }
        public bool IsTypeVisible(StatusType t) => !_hidden.Contains(t);
        public void SetTypeVisible(StatusType t, bool visible)
        {
            if (t == StatusType.Command) return;
            Enqueue(() =>
            {
                if (visible) _hidden.Remove(t); else _hidden.Add(t);
                RebuildDocument();
            });
        }
        internal void Touch(ConsoleLine line)
        {
            if (Interlocked.Exchange(ref line.Queued, 1) == 0)
                Enqueue(() => { line.Queued = 0; RenderLine(line); });
        }
        private void Enqueue(Action op)
        {
            _ops.Enqueue(op);
            ScheduleFlush();
        }
        private void ScheduleFlush()
        {
            if (Interlocked.Exchange(ref _flushScheduled, 1) != 0) return;
            Task.Delay(FlushIntervalMs).ContinueWith(
                _ => _ui.BeginInvoke(DispatcherPriority.Background, new Action(() => FlushCore(false))),
                TaskScheduler.Default);
        }
        public void Flush()
        {
            if (!_ui.CheckAccess()) { _ui.Invoke(() => FlushCore(true)); return; }
            FlushCore(true);
        }
        private void FlushCore(bool all)
        {
            Interlocked.Exchange(ref _flushScheduled, 0);
            if (_ops.IsEmpty) return;
            _box.BeginChange();
            try
            {
                int n = 0;
                while ((all || n++ < MaxOpsPerFlush) && _ops.TryDequeue(out var op))
                {
                    try { op(); } catch (Exception ex) { Debug.WriteLine(ex); }
                }
                Trim();
            }
            finally { _box.EndChange(); }
            if (!_ops.IsEmpty) ScheduleFlush();
        }
        private bool IsVisible(ConsoleLine l) => l.Type == StatusType.Command || !_hidden.Contains(l.Type);
        private void AddLine(ConsoleLine line)
        {
            _lines.AddLast(line);
            if (!IsVisible(line)) return;
            var p = NewParagraph(line);
            line.Paragraph = p;
            if (_inputPara != null) _box.Document.Blocks.InsertBefore(_inputPara, p);
            else _box.Document.Blocks.Add(p);
        }
        private void RenderLine(ConsoleLine line)
        {
            if (line.Removed) return;
            var p = line.Paragraph;
            if (!IsVisible(line))
            {
                if (p != null) { _box.Document.Blocks.Remove(p); line.Paragraph = null; }
                return;
            }
            if (p == null) { RebuildDocument(); return; }
            Fill(p, line);
        }
        private void Trim()
        {
            int excess = _lines.Count - MaxLines;
            while (excess-- > 0 && _lines.First != null)
            {
                var l = _lines.First.Value;
                _lines.RemoveFirst();
                l.Removed = true;
                if (l.Paragraph != null) { _box.Document.Blocks.Remove(l.Paragraph); l.Paragraph = null; }
            }
        }
        private void ClearNow()
        {
            foreach (var l in _lines) { l.Removed = true; l.Paragraph = null; }
            _lines.Clear();
            _box.Document.Blocks.Clear();
            if (_inputPara != null) _box.Document.Blocks.Add(_inputPara);
        }
        private void RebuildDocument()
        {
            var blocks = new List<Block>(_lines.Count + 1);
            foreach (var l in _lines)
            {
                if (IsVisible(l)) { var p = NewParagraph(l); l.Paragraph = p; blocks.Add(p); }
                else l.Paragraph = null;
            }
            if (_inputPara != null) blocks.Add(_inputPara);
            _box.Document.Blocks.Clear();
            _box.Document.Blocks.AddRange(blocks);
        }
        private Paragraph NewParagraph(ConsoleLine line)
        {
            var p = new Paragraph { Margin = new Thickness(0) };
            Fill(p, line);
            return p;
        }
        private void Fill(Paragraph p, ConsoleLine line)
        {
            var s = line.Snapshot();
            Brush brush = TypeBrushes[(int)s.Type];
            p.Inlines.Clear();
            if (_showTimestamp)
                p.Inlines.Add(new Run($"[{s.Time:HH:mm:ss}] ") { Foreground = DimBrush });
            if (!s.IsRaw)
            {
                string pre = PrefixFor(s.Type);
                if (pre.Length > 0) p.Inlines.Add(new Run(pre + " ") { Foreground = brush });
            }
            AddText(p, s.Text, s.IsRaw ? InputBrushFor(s.Type) : brush);
            if (s.LinkPath != null) p.Inlines.Add(CreateLink(s.LinkPath));
            if (s.Progress is double pr)
            {
                const int W = 24;
                int filled = (int)Math.Round(pr / 100.0 * W);
                p.Inlines.Add(new Run("  [") { Foreground = DimBrush });
                p.Inlines.Add(new Run(new string('█', filled)) { Foreground = brush });
                p.Inlines.Add(new Run(new string('░', W - filled)) { Foreground = DimBrush });
                p.Inlines.Add(new Run($"] {pr,3:0}%") { Foreground = brush });
            }
        }
        private static Brush InputBrushFor(StatusType t) => t == StatusType.Command ? TypeBrushes[(int)t] : TypeBrushes[(int)t];
        private static void AddText(Paragraph p, string text, Brush brush)
        {
            if (string.IsNullOrEmpty(text)) return;
            var parts = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) p.Inlines.Add(new LineBreak());
                if (parts[i].Length > 0) p.Inlines.Add(new Run(parts[i]) { Foreground = brush });
            }
        }
        private Hyperlink CreateLink(string path)
        {
            var link = new Hyperlink(new Run(path))
            {
                Foreground = LinkBrush,
                Cursor = Cursors.Hand,
                ToolTip = "Ctrl + Click to open"
            };
            link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
            link.MouseLeave += (_, _) => link.TextDecorations = null;
            link.Click += (_, _) => OpenPath(path);
            return link;
        }
        private void OpenPath(string path)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                Write("Hold Ctrl + Click to open");
                return;
            }
            try
            {
                if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                else if (File.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                else if (Directory.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                else
                    Write($"Path not found: {path}", StatusType.Warning);
            }
            catch (Exception ex) { Write($"Error: {ex.Message}", StatusType.Error); }
        }
        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
                _stick = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 2;
            if (_stick && AutoScroll && e.ExtentHeightChange != 0)
                _box.ScrollToEnd();
        }
        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            _box.FontSize = Math.Clamp(_box.FontSize + (e.Delta > 0 ? 1 : -1), 8, 32);
            e.Handled = true;
        }
        public async Task<string> ReadCommandAsync(string prompt = "> ")
            => await ReadAsync(prompt) ?? string.Empty;
        public async Task<bool> ConfirmAsync(string question, bool defaultYes = false)
        {
            string? r = await ReadAsync($"{question} {(defaultYes ? "[Y/n]" : "[y/N]")} ", new ConsoleReadOptions
            {
                AddToHistory = false,
                MaxLength = 3,
                Validator = s => s.Length == 0 || Regex.IsMatch(s, "^(y|yes|n|no)$", RegexOptions.IgnoreCase)
                    ? null : "Please type y or n"
            });
            if (r == null) return false;
            if (r.Length == 0) return defaultYes;
            return r.StartsWith("y", StringComparison.OrdinalIgnoreCase);
        }
        public async Task<string?> ReadAsync(string prompt = "> ", ConsoleReadOptions? options = null,
                                             CancellationToken ct = default)
        {
            try { await _readGate.WaitAsync(ct); }
            catch (OperationCanceledException) { return null; }
            try
            {
                var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                await _ui.InvokeAsync(() => BeginRead(prompt, options ?? new ConsoleReadOptions(), tcs));
                using (ct.Register(() => _ui.BeginInvoke(new Action(() => FinishRead(null, " ^C")))))
                    return await tcs.Task;
            }
            finally { _readGate.Release(); }
        }
        private void BeginRead(string prompt, ConsoleReadOptions o, TaskCompletionSource<string?> tcs)
        {
            FlushCore(true);
            _opts = o;
            _readTcs = tcs;
            _promptText = prompt;
            _buf.Clear();
            _buf.Append(o.MaxLength > 0 && o.DefaultText.Length > o.MaxLength
                ? o.DefaultText[..o.MaxLength] : o.DefaultText);
            _caret = _buf.Length;
            _histPos = _history.Count;
            _draft = "";
            _rBefore = new Run { Foreground = InputBrush };
            _rCaret = new Run(" ");
            _rAfter = new Run { Foreground = InputBrush };
            _inputPara = new Paragraph { Margin = new Thickness(0) };
            _inputPara.Inlines.Add(new Run(prompt) { Foreground = TypeBrushes[(int)StatusType.Command] });
            _inputPara.Inlines.Add(_rBefore);
            _inputPara.Inlines.Add(_rCaret);
            _inputPara.Inlines.Add(_rAfter);
            _box.Document.Blocks.Add(_inputPara);
            _stick = true;
            _caretOn = true;
            RenderInput();
            _blink.Start();
            PlaySound(PromptSound);
            InputRequested?.Invoke();
            _box.Focus();
            Keyboard.Focus(_box);
            _box.ScrollToEnd();
        }
        private void FinishRead(string? result, string echoSuffix)
        {
            var tcs = _readTcs;
            if (tcs == null) return;
            FlushCore(true);
            _blink.Stop();
            var o = _opts!;
            string shown = o.IsPassword ? new string('•', _buf.Length) : _buf.ToString();
            if (_inputPara != null) { _box.Document.Blocks.Remove(_inputPara); _inputPara = null; }
            _readTcs = null;
            _opts = null;
            AddLine(new ConsoleLine(this, _promptText + shown + echoSuffix, StatusType.Command, isRaw: true));
            if (result != null && o.AddToHistory && !o.IsPassword && result.Length > 0 &&
                (_history.Count == 0 || _history[^1] != result))
            {
                _history.Add(result);
                if (_history.Count > 200) _history.RemoveAt(0);
            }
            tcs.TrySetResult(result);
        }
        private void CommitInput()
        {
            string text = _buf.ToString();
            string? err = _opts?.Validator?.Invoke(text);
            if (err != null)
            {
                AddLine(new ConsoleLine(this, err, StatusType.Warning));
                PlaySound(ConsoleSound.Exclamation);
                return;
            }
            FinishRead(text, "");
        }
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl && e.Key == Key.L) { Clear(); e.Handled = true; return; }
            if (ctrl && e.Key == Key.D0) { _box.ClearValue(Control.FontSizeProperty); e.Handled = true; return; }
            if (_readTcs == null) return;
            switch (e.Key)
            {
                case Key.Enter: CommitInput(); break;
                case Key.Back:
                    if (ctrl) { int s = WordLeft(_caret); _buf.Remove(s, _caret - s); _caret = s; }
                    else if (_caret > 0) _buf.Remove(--_caret, 1);
                    Changed(); break;
                case Key.Delete:
                    if (_caret < _buf.Length) _buf.Remove(_caret, 1);
                    Changed(); break;
                case Key.Left: _caret = ctrl ? WordLeft(_caret) : Math.Max(0, _caret - 1); Changed(); break;
                case Key.Right: _caret = ctrl ? WordRight(_caret) : Math.Min(_buf.Length, _caret + 1); Changed(); break;
                case Key.Home when !ctrl: _caret = 0; Changed(); break;
                case Key.End when !ctrl: _caret = _buf.Length; Changed(); break;
                case Key.Up when !ctrl: HistoryMove(-1); break;
                case Key.Down when !ctrl: HistoryMove(+1); break;
                case Key.Tab: Complete(); break;
                case Key.Escape: SetBuffer(""); break;
                case Key.V when ctrl: Paste(); break;
                case Key.C when ctrl && _box.Selection.IsEmpty: FinishRead(null, " ^C"); break;
                default: return;
            }
            e.Handled = true;
        }
        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (_readTcs == null) return;
            e.Handled = true;
            bool any = false;
            foreach (char c in e.Text) any |= InsertChar(c);
            if (any) Changed();
        }
        private bool InsertChar(char c)
        {
            var o = _opts!;
            if (char.IsControl(c)) return false;
            if (o.MaxLength > 0 && _buf.Length >= o.MaxLength) return false;
            if (o.CharFilter != null && !o.CharFilter(c)) return false;
            _buf.Insert(_caret++, c);
            return true;
        }
        private void Paste()
        {
            try
            {
                if (!Clipboard.ContainsText()) return;
                string t = Clipboard.GetText().Split('\r', '\n')[0];
                bool any = false;
                foreach (char c in t) any |= InsertChar(c);
                if (any) Changed();
            }
            catch (Exception) { }
        }
        private void HistoryMove(int dir)
        {
            if (_history.Count == 0 || _opts!.IsPassword) return;
            if (_histPos == _history.Count) _draft = _buf.ToString();
            int pos = Math.Clamp(_histPos + dir, 0, _history.Count);
            if (pos == _histPos) return;
            _histPos = pos;
            SetBuffer(pos == _history.Count ? _draft : _history[pos], keepHistoryPos: true);
        }
        private void Complete()
        {
            if (Completer == null || _opts!.IsPassword) return;
            List<string> matches;
            try { matches = Completer(_buf.ToString()).Distinct().ToList(); }
            catch { return; }
            if (matches.Count == 0) return;
            string common = matches[0];
            foreach (var m in matches)
            {
                int i = 0;
                while (i < common.Length && i < m.Length && char.ToLowerInvariant(common[i]) == char.ToLowerInvariant(m[i])) i++;
                common = common[..i];
            }
            if (common.Length > _buf.Length) SetBuffer(common);
            else if (matches.Count > 1)
                AddLine(new ConsoleLine(this, string.Join("   ", matches), StatusType.Info));
        }
        private void SetBuffer(string text, bool keepHistoryPos = false)
        {
            _buf.Clear();
            _buf.Append(_opts!.MaxLength > 0 && text.Length > _opts.MaxLength ? text[.._opts.MaxLength] : text);
            _caret = _buf.Length;
            if (!keepHistoryPos) _histPos = _history.Count;
            Changed();
        }
        private int WordLeft(int i)
        {
            while (i > 0 && _buf[i - 1] == ' ') i--;
            while (i > 0 && _buf[i - 1] != ' ') i--;
            return i;
        }
        private int WordRight(int i)
        {
            while (i < _buf.Length && _buf[i] != ' ') i++;
            while (i < _buf.Length && _buf[i] == ' ') i++;
            return i;
        }
        private void Changed()
        {
            _caretOn = true;
            RenderInput();
        }
        private void RenderInput()
        {
            if (_rBefore == null || _rCaret == null || _rAfter == null || _opts == null) return;
            string text = _opts.IsPassword ? new string('•', _buf.Length) : _buf.ToString();
            bool atEnd = _caret >= text.Length;
            _rBefore.Text = text[.._caret];
            _rCaret.Text = atEnd ? " " : text[_caret].ToString();
            _rAfter.Text = atEnd ? "" : text[(_caret + 1)..];
            ApplyCaretVisual();
        }
        private void ApplyCaretVisual()
        {
            if (_rCaret == null) return;
            _rCaret.Background = _caretOn ? CaretBg : null;
            _rCaret.Foreground = _caretOn ? CaretFg : InputBrush;
        }
        public string GetLogText(bool withTimestamp = true)
        {
            Flush();
            return string.Join(Environment.NewLine, _lines.Select(l => l.ToPlainText(withTimestamp)));
        }
        public void SaveLog(string path) => File.WriteAllText(path, GetLogText(true), Encoding.UTF8);
        private void InitContextMenu()
        {
            var menu = new ContextMenu();
            var refresh = new List<Action>();
            MenuItem Item(ItemsControl parent, string header, Action click)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += (_, _) => click();
                parent.Items.Add(mi);
                return mi;
            }
            MenuItem Check(ItemsControl parent, string header, Func<bool> get, Action<bool> set)
            {
                var mi = new MenuItem { Header = header, IsCheckable = true };
                mi.Click += (_, _) => set(mi.IsChecked);
                refresh.Add(() => mi.IsChecked = get());
                parent.Items.Add(mi);
                return mi;
            }
            Item(menu, "Copy Selected", () => { if (!_box.Selection.IsEmpty) CopyText(_box.Selection.Text); });
            Item(menu, "Copy All", () => CopyText(GetLogText(ShowTimestamp)));
            Item(menu, "Select All", () => _box.SelectAll());
            menu.Items.Add(new Separator());
            Item(menu, "Clear All  (Ctrl+L)", Clear);
            Item(menu, "Save Log As...", () =>
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "Log (*.log;*.txt)|*.log;*.txt",
                    FileName = $"console_{DateTime.Now:yyyyMMdd_HHmmss}.log"
                };
                if (dlg.ShowDialog() == true) SaveLog(dlg.FileName);
            });
            menu.Items.Add(new Separator());
            Check(menu, "Word Wrap", () => WordWrap, v => WordWrap = v);
            Check(menu, "Auto-scroll", () => AutoScroll, v => { AutoScroll = v; if (v) ScrollToEnd(); });
            Check(menu, "Timestamps", () => ShowTimestamp, v => ShowTimestamp = v);
            Item(menu, "Reset Zoom  (Ctrl+0)", () => _box.ClearValue(Control.FontSizeProperty));
            menu.Opened += (_, _) => refresh.ForEach(a => a());
            _box.ContextMenu = menu;
        }
        private static void CopyText(string text)
        {
            try { Clipboard.SetText(text); } catch (Exception) { }
        }
        private static void PlaySound(ConsoleSound s)
        {
            switch (s)
            {
                case ConsoleSound.Asterisk: SystemSounds.Asterisk.Play(); break;
                case ConsoleSound.Beep: SystemSounds.Beep.Play(); break;
                case ConsoleSound.Exclamation: SystemSounds.Exclamation.Play(); break;
                case ConsoleSound.Hand: SystemSounds.Hand.Play(); break;
                case ConsoleSound.Question: SystemSounds.Question.Play(); break;
            }
        }
        private static StatusType DetectType(string message)
        {
            message = message.ToLowerInvariant();
            if (Regex.IsMatch(message, @"\b(error|fail|exception|err|not)\b")) return StatusType.Error;
            if (Regex.IsMatch(message, @"\b(warning|warn)\b")) return StatusType.Warning;
            if (Regex.IsMatch(message, @"\b(success|ok|done)\b")) return StatusType.Success;
            if (message.StartsWith('>')) return StatusType.Command;
            return StatusType.Info;
        }
        internal static string PrefixFor(StatusType type) => type switch
        {
            StatusType.Success => "[OK]",
            StatusType.Warning => "[WARN]",
            StatusType.Error => "[ERR]",
            StatusType.Command => ">",
            _ => "[INFO]"
        };
        private static Brush Frozen(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
            b.Freeze();
            return b;
        }
    }
}