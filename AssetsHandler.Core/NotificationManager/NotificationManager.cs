using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace AssetsHandler.Core.NotificationManager
{
    public static class NotificationManager
    {
        private static readonly ConcurrentDictionary<string, NotificationItem> _keyed = new();

        /// <summary>Set once at application startup. If null, the notification will still process without showing a toast.</summary>
        public static INotificationHost? Host { get; set; }

        /// <summary>Bound to the UI. Modified exclusively on the UI thread via Host.Post.</summary>
        public static ObservableCollection<NotificationItem> Items { get; } = new();
        public static Func<NotificationItem, bool>? ShowFilter { get; set; }
        public static int MaxVisible { get; set; } = 5;
        public static int ExitAnimationMs { get; set; } = 200;

        public static int SuccessMs { get; set; } = 4000;
        public static int InfoMs { get; set; } = 4000;
        public static int WarningMs { get; set; } = 6000;
        public static int ErrorMs { get; set; } = 8000;

        public static event Action<NotificationItem>? Shown;
        public static event Action<NotificationItem>? Closed;

        internal static void Post(Action a)
        {
            var h = Host;
            if (h == null) a(); else h.Post(a);
        }

        /// <param name="durationMs">null = default duration for the type, 0 = persistent until manually dismissed.</param>
        /// <param name="key">Replaces any existing notification that shares the same key.</param>
        public static NotificationItem Show(string title, string message,
            NotificationType type = NotificationType.Info,
            int? durationMs = null, Action? onClick = null, string? key = null)
        {
            var item = new NotificationItem(title, message, type, durationMs ?? DefaultFor(type), onClick, key);

            if (key != null)
            {
                if (_keyed.TryGetValue(key, out var old)) old.Dismiss();
                _keyed[key] = item;
            }

            Post(() =>
            {
                if (ShowFilter != null && !ShowFilter(item))
                {
                    if (key != null)
                        _keyed.TryRemove(new KeyValuePair<string, NotificationItem>(key, item));
                    Shown?.Invoke(item);
                    return;
                }

                Items.Add(item);
                item.StartTimer();
                Trim();
                Shown?.Invoke(item);
            });
            return item;
        }

        public static NotificationItem Success(string message, string title = "Success",
            int? durationMs = null, Action? onClick = null, string? key = null)
            => Show(title, message, NotificationType.Success, durationMs, onClick, key);

        public static NotificationItem Info(string message, string title = "Info",
            int? durationMs = null, Action? onClick = null, string? key = null)
            => Show(title, message, NotificationType.Info, durationMs, onClick, key);

        public static NotificationItem Warning(string message, string title = "Warning",
            int? durationMs = null, Action? onClick = null, string? key = null)
            => Show(title, message, NotificationType.Warning, durationMs, onClick, key);

        public static NotificationItem Error(string message, string title = "Error",
            int? durationMs = null, Action? onClick = null, string? key = null)
            => Show(title, message, NotificationType.Error, durationMs, onClick, key);

        public static void DismissAll()
            => Post(() => { foreach (var i in Items.ToList()) i.Dismiss(); });

        private static int DefaultFor(NotificationType t) => t switch
        {
            NotificationType.Success => SuccessMs,
            NotificationType.Warning => WarningMs,
            NotificationType.Error => ErrorMs,
            _ => InfoMs
        };

        private static void Trim()
        {
            var alive = Items.Where(i => !i.IsDismissed).ToList();
            int excess = alive.Count - MaxVisible;
            if (excess <= 0) return;

            var candidates = alive
                .Take(alive.Count - 1)
                .Where(i => i.DurationMs > 0);

            foreach (var old in candidates)
            {
                if (excess-- <= 0) break;
                old.Dismiss();
            }
        }

        internal static void Remove(NotificationItem item) => Post(() =>
        {
            Items.Remove(item);
            if (item.Key != null)
                _keyed.TryRemove(new KeyValuePair<string, NotificationItem>(item.Key, item));
            Closed?.Invoke(item);
        });
    }
}