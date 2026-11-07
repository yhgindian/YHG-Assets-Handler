using AssetsHandler.Controls;
using AssetsHandler.Core;
using AssetsHandler.Core.ProcessManager;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace YHG_Assets_Handler.Views.Windows
{
    public partial class ProcessManagerWindow : DarkWindow
    {
        private const double Gap = 16;
        private bool _allowClose, _placing, _userMoved;
        private Window? _followed;

        public ProcessManagerWindow()
        {
            InitializeComponent();

            ProcessManager.Processes.CollectionChanged += OnItemsChanged;
            UpdateEmpty();

            SizeChanged += (_, _) => Reposition();
            LocationChanged += (_, _) => { if (!_placing && IsLoaded) _userMoved = true; };
            Closed += (_, _) =>
            {
                ProcessManager.Processes.CollectionChanged -= OnItemsChanged;
                Unfollow();
            };
        }

        internal void AllowClose() => _allowClose = true;

        // X dabane par destroy nahi, sirf hide
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose) { e.Cancel = true; Hide(); return; }
            base.OnClosing(e);
        }

        internal void FollowOwner(Window? owner)
        {
            Unfollow();
            _followed = owner;
            if (owner == null) { Topmost = true; return; }

            owner.LocationChanged += OwnerMoved;
            owner.SizeChanged += OwnerMoved;
            owner.StateChanged += OwnerMoved;
            Reposition();
        }

        private void Unfollow()
        {
            if (_followed == null) return;
            _followed.LocationChanged -= OwnerMoved;
            _followed.SizeChanged -= OwnerMoved;
            _followed.StateChanged -= OwnerMoved;
            _followed = null;
        }

        private void OwnerMoved(object? s, EventArgs e) => Reposition();

        private void Reposition()
        {
            if (_userMoved) return;

            Rect area;
            var o = _followed;
            if (o != null && o.IsVisible && o.WindowState != WindowState.Minimized && o.ActualWidth > 0)
            {
                var dpi = VisualTreeHelper.GetDpi(o);
                var tl = o.PointToScreen(new Point(0, 0));
                area = new Rect(tl.X / dpi.DpiScaleX, tl.Y / dpi.DpiScaleY, o.ActualWidth, o.ActualHeight);
            }
            else area = SystemParameters.WorkArea;

            double h = ActualHeight > 0 ? ActualHeight : MinHeight;

            _placing = true;
            Left = area.Right - Width - Gap;
            Top = area.Bottom - h - Gap - 24;   // 24 = status bar
            _placing = false;
        }

        private void OnItemsChanged(object? s, NotifyCollectionChangedEventArgs e) => UpdateEmpty();

        private void UpdateEmpty()
            => EmptyText.Visibility = ProcessManager.Processes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}