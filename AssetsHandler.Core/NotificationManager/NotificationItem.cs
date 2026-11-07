using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;

namespace AssetsHandler.Core.NotificationManager
{
    public enum NotificationType { Info, Success, Warning, Error }

    public class NotificationItem : INotifyPropertyChanged
    {
        private readonly object _gate = new();
        private readonly Action? _onClick;
        private readonly Stopwatch _sw = new();
        private Timer? _timer;
        private int _remaining;
        private bool _paused;
        private bool _isClosing;
        private int _dismissed;

        internal NotificationItem(string title, string message, NotificationType type,
                                  int durationMs, Action? onClick, string? key)
        {
            Title = title;
            Message = message;
            Type = type;
            DurationMs = durationMs;
            Key = key;
            _onClick = onClick;
            DismissCommand = new SimpleCommand(Dismiss);
            ClickCommand = new SimpleCommand(Click);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Title { get; }
        public string Message { get; }
        public NotificationType Type { get; }
        public int DurationMs { get; }          // 0 = persistent / sticky
        public string? Key { get; }
        public bool HasClickAction => _onClick != null;
        public ICommand DismissCommand { get; }
        public ICommand ClickCommand { get; }

        /// <summary>
        /// Indicates whether the exit animation should be triggered by the UI.
        /// </summary>
        public bool IsClosing
        {
            get => _isClosing;
            internal set { if (_isClosing != value) { _isClosing = value; Raise(nameof(IsClosing)); } }
        }

        internal bool IsDismissed => Volatile.Read(ref _dismissed) != 0;

        // Hover-aware auto-dismiss timer controls

        internal void StartTimer()
        {
            if (DurationMs <= 0) return;
            lock (_gate) { _remaining = DurationMs; Arm(); }
        }

        private void Arm()
        {
            _timer?.Dispose();
            _sw.Restart();
            _timer = new Timer(_ => Dismiss(), null, _remaining, Timeout.Infinite);
        }

        public void Pause()
        {
            lock (_gate)
            {
                if (_timer == null || _paused || IsDismissed) return;
                _paused = true;
                _timer.Dispose();
                _timer = null;
                _remaining = Math.Max(50, _remaining - (int)_sw.ElapsedMilliseconds);
            }
        }

        public void Resume()
        {
            lock (_gate)
            {
                if (!_paused || IsDismissed) return;
                _paused = false;
                Arm();
            }
        }

        public void Click()
        {
            try { _onClick?.Invoke(); } catch { }
            Dismiss();
        }

        public void Dismiss()
        {
            if (Interlocked.Exchange(ref _dismissed, 1) != 0) return;

            lock (_gate) { _timer?.Dispose(); _timer = null; }

            NotificationManager.Post(() => IsClosing = true);
            Task.Delay(NotificationManager.ExitAnimationMs)
                .ContinueWith(_ => NotificationManager.Remove(this), TaskScheduler.Default);
        }

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private sealed class SimpleCommand : ICommand
        {
            private readonly Action _run;
            public SimpleCommand(Action run) => _run = run;
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _run();
        }
    }
}