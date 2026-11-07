using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AssetsHandler.Controls
{
    [Flags]
    public enum DarkWindowButtons
    {
        None = 0,
        Minimize = 1,
        Maximize = 2,
        Close = 4,
        All = Minimize | Maximize | Close
    }
    public enum DarkWindowIconMode
    {
        None,
        AppIcon,
        Custom
    }
    public class DarkWindow : Window
    {
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;
        private const int SC_MAXIMIZE = 0xF030;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        private DarkButton_01? _minBtn, _maxBtn, _closeBtn;
        private bool _forceClose;
        static DarkWindow()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DarkWindow),
                new FrameworkPropertyMetadata(typeof(DarkWindow)));
        }
        public DarkWindow()
        {
            SourceInitialized += OnSourceInitialized;
            DependencyPropertyDescriptor.FromProperty(IconProperty, typeof(Window)).AddValueChanged(this, (_, _) => UpdateTitleBarIcon());
            UpdateTitleBarIcon();
        }
        #region Properties
        public DarkWindowButtons ShowButtons
        {
            get { return (DarkWindowButtons)GetValue(ShowButtonsProperty); }
            set { SetValue(ShowButtonsProperty, value); }
        }
        public static readonly DependencyProperty ShowButtonsProperty =
            DependencyProperty.Register(nameof(ShowButtons), typeof(DarkWindowButtons), typeof(DarkWindow),
                new PropertyMetadata(DarkWindowButtons.All, OnButtonsChanged));
        public DarkWindowButtons EnabledButtons
        {
            get { return (DarkWindowButtons)GetValue(EnabledButtonsProperty); }
            set { SetValue(EnabledButtonsProperty, value); }
        }
        public static readonly DependencyProperty EnabledButtonsProperty =
            DependencyProperty.Register(nameof(EnabledButtons), typeof(DarkWindowButtons), typeof(DarkWindow),
                new PropertyMetadata(DarkWindowButtons.All, OnButtonsChanged));
        private static void OnButtonsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((DarkWindow)d).UpdateButtons();
        #endregion
        #region Template
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if (_minBtn != null) _minBtn.Click -= MinClick;
            if (_maxBtn != null) _maxBtn.Click -= MaxClick;
            if (_closeBtn != null) _closeBtn.Click -= CloseClick;
            _minBtn = GetTemplateChild("PART_MinimizeButton") as DarkButton_01;
            _maxBtn = GetTemplateChild("PART_MaximizeButton") as DarkButton_01;
            _closeBtn = GetTemplateChild("PART_CloseButton") as DarkButton_01;
            if (_minBtn != null) _minBtn.Click += MinClick;
            if (_maxBtn != null) _maxBtn.Click += MaxClick;
            if (_closeBtn != null) _closeBtn.Click += CloseClick;
            UpdateButtons();
        }
        private void MinClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
        private void MaxClick(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }
        private void CloseClick(object sender, RoutedEventArgs e) => Close();
        private void UpdateButtons()
        {
            Apply(_minBtn, DarkWindowButtons.Minimize);
            Apply(_maxBtn, DarkWindowButtons.Maximize);
            Apply(_closeBtn, DarkWindowButtons.Close);
            void Apply(DarkButton_01? btn, DarkWindowButtons flag)
            {
                if (btn == null) return;
                btn.Visibility = ShowButtons.HasFlag(flag) ? Visibility.Visible : Visibility.Collapsed;
                btn.IsEnabled = EnabledButtons.HasFlag(flag);
            }
        }
        private bool IsAllowed(DarkWindowButtons flag)
            => ShowButtons.HasFlag(flag) && EnabledButtons.HasFlag(flag);
        #endregion
        #region Native behavior
        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        }
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SYSCOMMAND)
            {
                int cmd = (int)(wParam.ToInt64() & 0xFFF0);
                if ((cmd == SC_MAXIMIZE && !IsAllowed(DarkWindowButtons.Maximize)) ||
                    (cmd == SC_MINIMIZE && !IsAllowed(DarkWindowButtons.Minimize)))
                {
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }
        #endregion
        #region Close control
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_forceClose && !IsAllowed(DarkWindowButtons.Close))
                e.Cancel = true;
            base.OnClosing(e);
        }
        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }
        #endregion
        #region Icon
        public DarkWindowIconMode IconMode
        {
            get { return (DarkWindowIconMode)GetValue(IconModeProperty); }
            set { SetValue(IconModeProperty, value); }
        }
        public static readonly DependencyProperty IconModeProperty =
            DependencyProperty.Register(nameof(IconMode), typeof(DarkWindowIconMode), typeof(DarkWindow),
                new PropertyMetadata(DarkWindowIconMode.AppIcon, OnIconChanged));
        public ImageSource? CustomIcon
        {
            get { return (ImageSource?)GetValue(CustomIconProperty); }
            set { SetValue(CustomIconProperty, value); }
        }
        public static readonly DependencyProperty CustomIconProperty =
            DependencyProperty.Register(nameof(CustomIcon), typeof(ImageSource), typeof(DarkWindow),
                new PropertyMetadata(null, OnIconChanged));
        private static readonly DependencyPropertyKey TitleBarIconPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(TitleBarIcon), typeof(ImageSource), typeof(DarkWindow),
                new PropertyMetadata(null));
        public static readonly DependencyProperty TitleBarIconProperty = TitleBarIconPropertyKey.DependencyProperty;
        public ImageSource? TitleBarIcon => (ImageSource?)GetValue(TitleBarIconProperty);
        private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((DarkWindow)d).UpdateTitleBarIcon();
        private void UpdateTitleBarIcon()
        {
            ImageSource? src = IconMode switch
            {
                DarkWindowIconMode.AppIcon => Icon ?? AppIconCache.Value,
                DarkWindowIconMode.Custom => CustomIcon,
                _ => null
            };
            SetValue(TitleBarIconPropertyKey, src);
        }
        private static readonly Lazy<ImageSource?> AppIconCache = new(() =>
        {
            var fromExe = TryExtractExeIcon();
            if (fromExe != null) return fromExe;
            return ToImageSource(System.Drawing.SystemIcons.Application);
        });
        private static ImageSource? TryExtractExeIcon()
        {
            try
            {
                string? path = Environment.ProcessPath;
                if (string.IsNullOrEmpty(path)) return null;
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                return icon == null ? null : ToImageSource(icon);
            }
            catch { return null; }
        }
        private static ImageSource? ToImageSource(System.Drawing.Icon icon)
        {
            try
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
        #endregion
    }
}