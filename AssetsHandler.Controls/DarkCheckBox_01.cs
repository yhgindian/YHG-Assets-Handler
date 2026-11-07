using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AssetsHandler.Controls
{
    public class DarkCheckBox_01 : CheckBox
    {
        static DarkCheckBox_01()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DarkCheckBox_01),
                new FrameworkPropertyMetadata(typeof(DarkCheckBox_01)));
        }
        private static DependencyProperty Reg<T>(string name, T def) =>
            DependencyProperty.Register(name, typeof(T), typeof(DarkCheckBox_01), new PropertyMetadata(def));
        public static readonly DependencyProperty OnHoverBgProperty = Reg<Brush?>(nameof(OnHoverBg), null);
        public static readonly DependencyProperty OnHoverBorderProperty = Reg<Brush?>(nameof(OnHoverBorder), null);
        public static readonly DependencyProperty OnHoverForegroundProperty = Reg<Brush?>(nameof(OnHoverForeground), null);
        public static readonly DependencyProperty CheckedBgProperty = Reg<Brush?>(nameof(CheckedBg), null);
        public static readonly DependencyProperty CheckedBorderProperty = Reg<Brush?>(nameof(CheckedBorder), null);
        public static readonly DependencyProperty CheckMarkBrushProperty = Reg<Brush?>(nameof(CheckMarkBrush), null);
        public static readonly DependencyProperty BoxSizeProperty = Reg(nameof(BoxSize), 18.0);
        public static readonly DependencyProperty CornerRadiusProperty = Reg(nameof(CornerRadius), new CornerRadius(4));
        public Brush? OnHoverBg { get => (Brush?)GetValue(OnHoverBgProperty); set => SetValue(OnHoverBgProperty, value); }
        public Brush? OnHoverBorder { get => (Brush?)GetValue(OnHoverBorderProperty); set => SetValue(OnHoverBorderProperty, value); }
        public Brush? OnHoverForeground { get => (Brush?)GetValue(OnHoverForegroundProperty); set => SetValue(OnHoverForegroundProperty, value); }
        public Brush? CheckedBg { get => (Brush?)GetValue(CheckedBgProperty); set => SetValue(CheckedBgProperty, value); }
        public Brush? CheckedBorder { get => (Brush?)GetValue(CheckedBorderProperty); set => SetValue(CheckedBorderProperty, value); }
        public Brush? CheckMarkBrush { get => (Brush?)GetValue(CheckMarkBrushProperty); set => SetValue(CheckMarkBrushProperty, value); }
        public double BoxSize { get => (double)GetValue(BoxSizeProperty); set => SetValue(BoxSizeProperty, value); }
        public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    }
}