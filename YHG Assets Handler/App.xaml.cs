using System.Configuration;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace YHG_Assets_Handler
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // Suppress the default horizontal auto-scroll snap behavior when selecting nested TreeViewItems
            EventManager.RegisterClassHandler(
                typeof(TreeViewItem),
                FrameworkElement.RequestBringIntoViewEvent,
                new RequestBringIntoViewEventHandler(OnRequestBringIntoView));

            // Intercept mouse wheel messages on TreeViewItems for Shift + Wheel horizontal scrolling
            EventManager.RegisterClassHandler(
                typeof(TreeViewItem),
                UIElement.PreviewMouseWheelEvent,
                new MouseWheelEventHandler(TreeViewItem_PreviewMouseWheel));
        }

        private void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        private void TreeViewItem_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta != 0 && ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 || IsHorizontalSwipe(e)))
            {
                // Traverse up the visual tree from TreeViewItem to locate the owning TreeView
                DependencyObject parent = (DependencyObject)sender;
                while (parent != null && !(parent is TreeView))
                {
                    parent = VisualTreeHelper.GetParent(parent);
                }

                if (parent is TreeView treeView)
                {
                    var scrollViewer = GetScrollViewer(treeView);
                    if (scrollViewer != null)
                    {
                        if (e.Delta > 0)
                            scrollViewer.LineLeft();
                        else
                            scrollViewer.LineRight();

                        // Mark as handled to prevent simultaneous vertical scrolling
                        e.Handled = true;
                    }
                }
            }
        }

        private ScrollViewer? GetScrollViewer(DependencyObject depObj)
        {
            if (depObj is ScrollViewer scrollViewer) return scrollViewer;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private bool IsHorizontalSwipe(MouseWheelEventArgs e)
        {
            return false;
        }
    }
}