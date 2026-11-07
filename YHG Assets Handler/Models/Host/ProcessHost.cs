using AssetsHandler.Core;
using AssetsHandler.Core.ProcessManager;
using System.Windows;
using YHG_Assets_Handler.Views.Windows;

namespace YHG_Assets_Handler.Models.Host
{
    public sealed class ProcessHost : IProcessHost
    {
        private readonly Window _owner;
        private ProcessManagerWindow? _win;
        private bool _autoOpened;

        public ProcessHost(Window owner)
        {
            _owner = owner;
            owner.Closing += (_, e) => { if (!e.Cancel) _win?.AllowClose(); };
        }

        public void Post(Action action)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) action();
            else d.BeginInvoke(action);
        }

        public void Show(bool auto)
        {
            if (Application.Current == null) return;

            if (_win == null)
            {
                var w = new ProcessManagerWindow();
                if (_owner.IsLoaded) w.Owner = _owner;
                w.FollowOwner(w.Owner);
                w.Closed += (_, _) => { if (ReferenceEquals(_win, w)) _win = null; };
                _win = w;
            }

            if (!_win.IsVisible)
            {
                _autoOpened = auto;
                _win.Show();
            }
            else if (!auto) _autoOpened = false;
        }

        public void Hide(bool onlyIfAuto)
        {
            if (onlyIfAuto && !_autoOpened) return;
            _autoOpened = false;
            _win?.Hide();
        }

        public void Toggle()
        {
            if (_win != null && _win.IsVisible) Hide(onlyIfAuto: false);
            else Show(auto: false);
        }
    }
}