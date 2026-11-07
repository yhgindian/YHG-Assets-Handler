using System.Windows;
using System.Windows.Controls;

namespace AssetsHandler.Controls
{
    public class DarkMenu_01 : Menu
    {
        static DarkMenu_01()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DarkMenu_01),
                new FrameworkPropertyMetadata(typeof(DarkMenu_01)));
        }
        protected override DependencyObject GetContainerForItemOverride() => new DarkMenuItem_01();
        protected override bool IsItemItsOwnContainerOverride(object item) => item is MenuItem or Separator;
    }
}