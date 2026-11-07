using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AssetsHandler.Controls
{
    public class DarkButton_01 : Button
    {
        static DarkButton_01()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DarkButton_01),
                new FrameworkPropertyMetadata(typeof(DarkButton_01)));
        }
        public Brush OnHoverBg
        {
            get { return (Brush)GetValue(OnHoverBgProperty); }
            set { SetValue(OnHoverBgProperty, value); }
        }
        public static readonly DependencyProperty OnHoverBgProperty =
            DependencyProperty.Register("OnHoverBg", typeof(Brush), typeof(DarkButton_01), new PropertyMetadata(null));
        public Brush OnHoverForeground
        {
            get { return (Brush)GetValue(OnHoverForegroundProperty); }
            set { SetValue(OnHoverForegroundProperty, value); }
        }
        public static readonly DependencyProperty OnHoverForegroundProperty =
            DependencyProperty.Register("OnHoverForeground", typeof(Brush), typeof(DarkButton_01), new PropertyMetadata(null));
        public Brush OnHoverBorder
        {
            get { return (Brush)GetValue(OnHoverBorderProperty); }
            set { SetValue(OnHoverBorderProperty, value); }
        }
        public static readonly DependencyProperty OnHoverBorderProperty =
            DependencyProperty.Register("OnHoverBorder", typeof(Brush), typeof(DarkButton_01), new PropertyMetadata(null));
        public CornerRadius CornerRadius
        {
            get { return (CornerRadius)GetValue(CornerRadiusProperty); }
            set { SetValue(CornerRadiusProperty, value); }
        }
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(DarkButton_01),
                new PropertyMetadata(new CornerRadius(4)));
    }
}