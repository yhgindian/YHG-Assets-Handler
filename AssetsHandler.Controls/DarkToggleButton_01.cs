using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AssetsHandler.Controls
{
    public class DarkToggleButton_01 : ToggleButton
    {
        public static readonly DependencyProperty OffColorProperty =
            DependencyProperty.Register(
                nameof(OffColor), typeof(Brush), typeof(DarkToggleButton_01),
                new PropertyMetadata(Brushes.Cyan));
        public Brush OffColor
        {
            get => (Brush)GetValue(OffColorProperty);
            set => SetValue(OffColorProperty, value);
        }
        public static readonly DependencyProperty OnColorProperty =
            DependencyProperty.Register(
                nameof(OnColor), typeof(Brush), typeof(DarkToggleButton_01),
                new PropertyMetadata(Brushes.Orange)); 
        public Brush OnColor
        {
            get => (Brush)GetValue(OnColorProperty);
            set => SetValue(OnColorProperty, value);
        }
        public static readonly DependencyProperty FillColorProperty =
            DependencyProperty.Register(
                nameof(FillColor), typeof(Brush), typeof(DarkToggleButton_01),
                new PropertyMetadata(null, OnFillColorChanged));
        public Brush FillColor
        {
            get => (Brush)GetValue(FillColorProperty);
            set => SetValue(FillColorProperty, value);
        }
        private static readonly DependencyPropertyKey HasFillPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(HasFill), typeof(bool), typeof(DarkToggleButton_01),
                new PropertyMetadata(false));
        public static readonly DependencyProperty HasFillProperty = HasFillPropertyKey.DependencyProperty;
        public bool HasFill => (bool)GetValue(HasFillProperty);
        private static void OnFillColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            d.SetValue(HasFillPropertyKey, e.NewValue != null);
        }
        static DarkToggleButton_01()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(DarkToggleButton_01),
                new FrameworkPropertyMetadata(typeof(DarkToggleButton_01)));
        }
    }
}