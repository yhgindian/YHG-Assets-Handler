using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AssetsHandler.Controls
{
    public class ProgressColorStop
    {
        public double Percent { get; set; }
        public Color Color { get; set; } = Colors.White;
    }
    public class ProgressClickEventArgs : RoutedEventArgs
    {
        public ProgressClickEventArgs(RoutedEvent routedEvent, object source,
            double percent, double value, bool isOnFilledPart) : base(routedEvent, source)
        {
            Percent = percent;
            Value = value;
            IsOnFilledPart = isOnFilledPart;
        }
        public double Percent { get; }
        public double Value { get; }
        public bool IsOnFilledPart { get; }
    }
    public delegate void ProgressClickEventHandler(object sender, ProgressClickEventArgs e);
    [TemplatePart(Name = "PART_Root", Type = typeof(Border))]
    [TemplatePart(Name = "PART_Rainbow", Type = typeof(Border))]
    public class DarkProgressBar_01 : ProgressBar
    {
        private const double RainbowPeriod = 160; 
        private Border? _root, _rainbow;
        private readonly ToolTip _tip = new();
        private readonly TranslateTransform _rainbowShift = new();
        private readonly LinearGradientBrush _rainbowBrush;
        private ProgressColorStop[] _sorted = Array.Empty<ProgressColorStop>();
        private SolidColorBrush? _rangeBrush;
        private bool _pressed;
        static DarkProgressBar_01()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DarkProgressBar_01),
                new FrameworkPropertyMetadata(typeof(DarkProgressBar_01)));
        }
        public DarkProgressBar_01()
        {
            ColorStops = new ObservableCollection<ProgressColorStop>();
            ColorStops.CollectionChanged += OnStopsChanged;
            _rainbowBrush = CreateRainbow(_rainbowShift);
            _tip.Background = Frozen(Color.FromRgb(0x2D, 0x2D, 0x30));
            _tip.Foreground = Frozen(Color.FromRgb(0xF1, 0xF1, 0xF1));
            _tip.BorderBrush = Frozen(Color.FromRgb(0x3F, 0x3F, 0x46));
            _tip.Padding = new Thickness(6, 3, 6, 3);
        }
        #region Colors
        public Brush TrackBrush
        {
            get => (Brush)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }
        public static readonly DependencyProperty TrackBrushProperty =
            DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(DarkProgressBar_01),
                new PropertyMetadata(null));
        public Brush FillBrush
        {
            get => (Brush)GetValue(FillBrushProperty);
            set => SetValue(FillBrushProperty, value);
        }
        public static readonly DependencyProperty FillBrushProperty =
            DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(DarkProgressBar_01),
                new PropertyMetadata(null, OnVisualChanged));
        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(DarkProgressBar_01),
                new PropertyMetadata(new CornerRadius(4)));
        public bool UseRangeColors
        {
            get => (bool)GetValue(UseRangeColorsProperty);
            set => SetValue(UseRangeColorsProperty, value);
        }
        public static readonly DependencyProperty UseRangeColorsProperty =
            DependencyProperty.Register(nameof(UseRangeColors), typeof(bool), typeof(DarkProgressBar_01),
                new PropertyMetadata(false, OnVisualChanged));
        public bool BlendRangeColors
        {
            get => (bool)GetValue(BlendRangeColorsProperty);
            set => SetValue(BlendRangeColorsProperty, value);
        }
        public static readonly DependencyProperty BlendRangeColorsProperty =
            DependencyProperty.Register(nameof(BlendRangeColors), typeof(bool), typeof(DarkProgressBar_01),
                new PropertyMetadata(false, OnVisualChanged));
        public ObservableCollection<ProgressColorStop> ColorStops { get; }
        private static readonly DependencyPropertyKey ActualFillPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(ActualFill), typeof(Brush), typeof(DarkProgressBar_01),
                new PropertyMetadata(null));
        public static readonly DependencyProperty ActualFillProperty = ActualFillPropertyKey.DependencyProperty;
        public Brush? ActualFill => (Brush?)GetValue(ActualFillProperty);
        #endregion
        #region RGB bar
        public bool IsRainbow
        {
            get => (bool)GetValue(IsRainbowProperty);
            set => SetValue(IsRainbowProperty, value);
        }
        public static readonly DependencyProperty IsRainbowProperty =
            DependencyProperty.Register(nameof(IsRainbow), typeof(bool), typeof(DarkProgressBar_01),
                new PropertyMetadata(false, OnRainbowChanged));
        public double RainbowSeconds
        {
            get => (double)GetValue(RainbowSecondsProperty);
            set => SetValue(RainbowSecondsProperty, value);
        }
        public static readonly DependencyProperty RainbowSecondsProperty =
            DependencyProperty.Register(nameof(RainbowSeconds), typeof(double), typeof(DarkProgressBar_01),
                new PropertyMetadata(3.0, OnRainbowChanged));
        #endregion
        #region Percent + ToolTip
        private static readonly DependencyPropertyKey PercentPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(Percent), typeof(double), typeof(DarkProgressBar_01),
                new PropertyMetadata(0.0));
        public static readonly DependencyProperty PercentProperty = PercentPropertyKey.DependencyProperty;
        public double Percent => (double)GetValue(PercentProperty);
        public bool ShowPercentToolTip
        {
            get => (bool)GetValue(ShowPercentToolTipProperty);
            set => SetValue(ShowPercentToolTipProperty, value);
        }
        public static readonly DependencyProperty ShowPercentToolTipProperty =
            DependencyProperty.Register(nameof(ShowPercentToolTip), typeof(bool), typeof(DarkProgressBar_01),
                new PropertyMetadata(true, (d, _) => ((DarkProgressBar_01)d).ApplyToolTipState()));
        public string ToolTipFormat
        {
            get => (string)GetValue(ToolTipFormatProperty);
            set => SetValue(ToolTipFormatProperty, value);
        }
        public static readonly DependencyProperty ToolTipFormatProperty =
            DependencyProperty.Register(nameof(ToolTipFormat), typeof(string), typeof(DarkProgressBar_01),
                new PropertyMetadata("{0:0}%", OnVisualChanged));
        #endregion
        #region Click
        public static readonly RoutedEvent ProgressClickEvent =
            EventManager.RegisterRoutedEvent(nameof(ProgressClick), RoutingStrategy.Bubble,
                typeof(ProgressClickEventHandler), typeof(DarkProgressBar_01));
        public event ProgressClickEventHandler ProgressClick
        {
            add => AddHandler(ProgressClickEvent, value);
            remove => RemoveHandler(ProgressClickEvent, value);
        }
        public ICommand? ClickCommand
        {
            get => (ICommand?)GetValue(ClickCommandProperty);
            set => SetValue(ClickCommandProperty, value);
        }
        public static readonly DependencyProperty ClickCommandProperty =
            DependencyProperty.Register(nameof(ClickCommand), typeof(ICommand), typeof(DarkProgressBar_01),
                new PropertyMetadata(null));
        public bool IsClickable
        {
            get => (bool)GetValue(IsClickableProperty);
            set => SetValue(IsClickableProperty, value);
        }
        public static readonly DependencyProperty IsClickableProperty =
            DependencyProperty.Register(nameof(IsClickable), typeof(bool), typeof(DarkProgressBar_01),
                new PropertyMetadata(true));
        public bool SeekOnClick
        {
            get => (bool)GetValue(SeekOnClickProperty);
            set => SetValue(SeekOnClickProperty, value);
        }
        public static readonly DependencyProperty SeekOnClickProperty =
            DependencyProperty.Register(nameof(SeekOnClick), typeof(bool), typeof(DarkProgressBar_01),
                new PropertyMetadata(false));
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (!IsClickable || !IsEnabled) return;
            _pressed = true;
            CaptureMouse();
            e.Handled = true;
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_pressed) return;
            _pressed = false;
            ReleaseMouseCapture();
            Point p = e.GetPosition(this);
            if (p.X < 0 || p.Y < 0 || p.X > ActualWidth || p.Y > ActualHeight) return;
            RaiseProgressClick(p.X);
            e.Handled = true;
        }
        private void RaiseProgressClick(double x)
        {
            if (ActualWidth <= 0) return;
            double pct = Math.Clamp(x / ActualWidth * 100.0, 0, 100);
            double val = Minimum + (Maximum - Minimum) * pct / 100.0;
            bool onFilled = pct <= Percent; 
            if (SeekOnClick && !IsIndeterminate)
                SetCurrentValue(ValueProperty, val);
            RaiseEvent(new ProgressClickEventArgs(ProgressClickEvent, this, pct, val, onFilled));
            if (ClickCommand?.CanExecute(val) == true)
                ClickCommand.Execute(val);
        }
        #endregion
        #region Template + updates
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _root = GetTemplateChild("PART_Root") as Border;
            _rainbow = GetTemplateChild("PART_Rainbow") as Border;
            if (_rainbow != null) _rainbow.Background = _rainbowBrush;
            if (_root != null) ToolTipService.SetInitialShowDelay(_root, 200);
            ApplyToolTipState();
            UpdateVisuals();
            UpdateRainbow();
        }
        protected override void OnValueChanged(double oldValue, double newValue)
        {
            base.OnValueChanged(oldValue, newValue);
            UpdateVisuals();
        }
        protected override void OnMinimumChanged(double oldMinimum, double newMinimum)
        {
            base.OnMinimumChanged(oldMinimum, newMinimum);
            UpdateVisuals();
        }
        protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
        {
            base.OnMaximumChanged(oldMaximum, newMaximum);
            UpdateVisuals();
        }
        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((DarkProgressBar_01)d).UpdateVisuals();
        private static void OnRainbowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((DarkProgressBar_01)d).UpdateRainbow();
        private void OnStopsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _sorted = ColorStops.OrderBy(s => s.Percent).ToArray();
            UpdateVisuals();
        }
        private void UpdateVisuals()
        {
            double range = Maximum - Minimum;
            double pct = range > 0 ? Math.Clamp((Value - Minimum) / range * 100.0, 0, 100) : 0;
            if (Math.Abs(Percent - pct) > double.Epsilon)
                SetValue(PercentPropertyKey, pct);
            Brush? fill;
            if (UseRangeColors && _sorted.Length > 0)
            {
                Color c = ResolveColor(pct);
                if (_rangeBrush == null || _rangeBrush.Color != c)
                {
                    _rangeBrush = new SolidColorBrush(c);
                    _rangeBrush.Freeze();
                }
                fill = _rangeBrush;
            }
            else
            {
                fill = FillBrush;
            }
            if (!ReferenceEquals(ActualFill, fill))
                SetValue(ActualFillPropertyKey, fill);
            string text;
            try { text = string.Format(CultureInfo.CurrentCulture, ToolTipFormat, pct); }
            catch (FormatException) { text = pct.ToString("0", CultureInfo.CurrentCulture) + "%"; }
            _tip.Content = text;
        }
        private void ApplyToolTipState()
        {
            if (_root != null)
                _root.ToolTip = ShowPercentToolTip ? _tip : null;
        }
        private void UpdateRainbow()
        {
            if (_rainbow == null) return;
            _rainbow.Visibility = IsRainbow ? Visibility.Visible : Visibility.Collapsed;
            if (IsRainbow)
            {
                var anim = new DoubleAnimation(0, RainbowPeriod, TimeSpan.FromSeconds(Math.Max(0.2, RainbowSeconds)))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                _rainbowShift.BeginAnimation(TranslateTransform.XProperty, anim);
            }
            else
            {
                _rainbowShift.BeginAnimation(TranslateTransform.XProperty, null);
            }
        }
        #endregion
        #region Helpers
        private Color ResolveColor(double p)
        {
            var s = _sorted;
            if (!BlendRangeColors)
            {
                for (int i = s.Length - 1; i >= 0; i--)
                    if (p >= s[i].Percent) return s[i].Color;
                return s[0].Color;
            }
            if (p <= s[0].Percent) return s[0].Color;
            if (p >= s[^1].Percent) return s[^1].Color;
            for (int i = 1; i < s.Length; i++)
            {
                if (p <= s[i].Percent)
                {
                    double span = s[i].Percent - s[i - 1].Percent;
                    double t = span <= 0 ? 1 : (p - s[i - 1].Percent) / span;
                    return Lerp(s[i - 1].Color, s[i].Color, t);
                }
            }
            return s[^1].Color;
        }
        private static Color Lerp(Color a, Color b, double t)
        {
            static byte L(byte x, byte y, double t) => (byte)Math.Round(x + (y - x) * t);
            return Color.FromArgb(L(a.A, b.A, t), L(a.R, b.R, t), L(a.G, b.G, t), L(a.B, b.B, t));
        }
        private static LinearGradientBrush CreateRainbow(TranslateTransform shift)
        {
            var brush = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(RainbowPeriod, 0),
                SpreadMethod = GradientSpreadMethod.Repeat,
                Transform = shift
            };
            Color[] cols = { Colors.Red, Colors.Yellow, Colors.Lime, Colors.Cyan, Colors.Blue, Colors.Magenta, Colors.Red };
            for (int i = 0; i < cols.Length; i++)
                brush.GradientStops.Add(new GradientStop(cols[i], i / (double)(cols.Length - 1)));
            return brush;
        }
        private static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        #endregion
    }
}