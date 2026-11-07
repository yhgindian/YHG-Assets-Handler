using System;
using System.Collections.Generic;
using System.Text;

namespace AssetsHandler.Core.NotificationManager
{
    public interface INotificationHost
    {
        void Post(Action action);
    }
}
