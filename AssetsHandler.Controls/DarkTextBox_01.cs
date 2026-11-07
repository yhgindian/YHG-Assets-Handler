using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AssetsHandler.Controls
{
    public class DarkTextBox_01 : TextBox
    {
        static DarkTextBox_01()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DarkTextBox_01),
                new FrameworkPropertyMetadata(typeof(DarkTextBox_01)));
        }
        public string Placeholder
        {
            get { return (string)GetValue(PlaceholderProperty); }
            set { SetValue(PlaceholderProperty, value); }
        }
        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(DarkTextBox_01),
                new PropertyMetadata("Enter value"));
        public Brush PlaceholderForeground
        {
            get { return (Brush)GetValue(PlaceholderForegroundProperty); }
            set { SetValue(PlaceholderForegroundProperty, value); }
        }
        public static readonly DependencyProperty PlaceholderForegroundProperty =
            DependencyProperty.Register(nameof(PlaceholderForeground), typeof(Brush), typeof(DarkTextBox_01),
                new PropertyMetadata(null));
        public Brush OnHoverBg
        {
            get { return (Brush)GetValue(OnHoverBgProperty); }
            set { SetValue(OnHoverBgProperty, value); }
        }
        public static readonly DependencyProperty OnHoverBgProperty =
            DependencyProperty.Register(nameof(OnHoverBg), typeof(Brush), typeof(DarkTextBox_01),
                new PropertyMetadata(null));
        public Brush OnHoverForeground
        {
            get { return (Brush)GetValue(OnHoverForegroundProperty); }
            set { SetValue(OnHoverForegroundProperty, value); }
        }
        public static readonly DependencyProperty OnHoverForegroundProperty =
            DependencyProperty.Register(nameof(OnHoverForeground), typeof(Brush), typeof(DarkTextBox_01),
                new PropertyMetadata(null));
        public Brush OnHoverBorder
        {
            get { return (Brush)GetValue(OnHoverBorderProperty); }
            set { SetValue(OnHoverBorderProperty, value); }
        }
        public static readonly DependencyProperty OnHoverBorderProperty =
            DependencyProperty.Register(nameof(OnHoverBorder), typeof(Brush), typeof(DarkTextBox_01),
                new PropertyMetadata(null));
    }
}