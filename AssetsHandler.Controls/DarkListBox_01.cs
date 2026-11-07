using System.Windows;
using System.Windows.Controls;

namespace AssetsHandler.Controls
{
    public class DarkListBox_01 : ListBox
    {
        static DarkListBox_01()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(DarkListBox_01),
                new FrameworkPropertyMetadata(typeof(DarkListBox_01)));
        }
        protected override DependencyObject GetContainerForItemOverride()
        {
            return new DarkListBoxItem_01();
        }
        protected override bool IsItemItsOwnContainerOverride(object item)
        {
            return item is DarkListBoxItem_01;
        }
    }
    public class DarkListBoxItem_01 : ListBoxItem
    {
        static DarkListBoxItem_01()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(DarkListBoxItem_01),
                new FrameworkPropertyMetadata(typeof(DarkListBoxItem_01)));
        }
    }
}