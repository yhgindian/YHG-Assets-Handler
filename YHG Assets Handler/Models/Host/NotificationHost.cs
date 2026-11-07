using System.Windows.Threading;

namespace YHG_Assets_Handler.Models.Host
{
    public sealed class NotificationHost : INotificationHost
    {
        private readonly Dispatcher _dispatcher;
        public NotificationHost(Dispatcher dispatcher) => _dispatcher = dispatcher;

        public void Post(Action action)
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.BeginInvoke(action);
        }
    }
}