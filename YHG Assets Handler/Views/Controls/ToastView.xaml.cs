using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AssetsHandler.Core.NotificationManager;
using MahApps.Metro.IconPacks;

namespace YHG_Assets_Handler.Views.Controls
{
    public partial class ToastView : UserControl
    {
        private static readonly Brush SuccessBrush = Frozen(0x2D, 0xD4, 0xA0);
        private static readonly Brush InfoBrush = Frozen(0x3B, 0x82, 0xF6);
        private static readonly Brush WarningBrush = Frozen(0xF5, 0x9E, 0x0B);
        private static readonly Brush ErrorBrush = Frozen(0xEF, 0x44, 0x44);

        private NotificationItem? _item;
        private AnimationClock? _barClock;
        private bool _started;

        public ToastView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            MouseEnter += (_, _) => { _item?.Pause(); _barClock?.Controller?.Pause(); };
            MouseLeave += (_, _) => { _item?.Resume(); _barClock?.Controller?.Resume(); };
            MouseLeftButtonUp += (_, _) => { if (_item is { HasClickAction: true } n) n.Click(); };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_started || DataContext is not NotificationItem n) return;
            _started = true;
            _item = n;
            n.PropertyChanged += OnItemChanged;

            // Type ke hisab se look
            (Brush accent, PackIconMaterialKind kind) = n.Type switch
            {
                NotificationType.Success => (SuccessBrush, PackIconMaterialKind.Check),
                NotificationType.Warning => (WarningBrush, PackIconMaterialKind.Exclamation),
                NotificationType.Error => (ErrorBrush, PackIconMaterialKind.Close),
                _ => (InfoBrush, PackIconMaterialKind.InformationVariant)
            };
            Stripe.Background = accent;
            IconBg.Fill = accent;
            TypeIcon.Foreground = accent;
            TypeIcon.Kind = kind;
            TimerBar.Background = accent;
            if (n.HasClickAction) Cursor = Cursors.Hand;

            // Enter animation: slide + fade
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

            // Countdown bar (sticky me nahi)
            if (n.DurationMs <= 0)
            {
                TimerBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                _barClock = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(n.DurationMs)).CreateClock();
                BarScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, _barClock);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_item != null) _item.PropertyChanged -= OnItemChanged;
            _item = null;
            _started = false;
        }

        // Exit animation: Core IsClosing=true karta hai, ~200ms baad row hata deta hai
        private void OnItemChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(NotificationItem.IsClosing) || _item is not { IsClosing: true }) return;

            Slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, 40, TimeSpan.FromMilliseconds(180)));
            Card.BeginAnimation(OpacityProperty,
                new DoubleAnimation(Card.Opacity, 0, TimeSpan.FromMilliseconds(180)));
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }
    }
}