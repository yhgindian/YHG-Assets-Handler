using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace AssetsHandler.Controls
{
    public static class WindowHelper
    {
        private static readonly Dictionary<(Type, object), Window> Keyed = new();
        public static IReadOnlyList<Window> All
            => Application.Current?.Windows.Cast<Window>().ToList() ?? new List<Window>();
        public static Window? Main => Application.Current?.MainWindow;
        public static Window? Active => All.FirstOrDefault(w => w.IsActive) ?? Main;
        public static T? Get<T>() where T : Window => All.OfType<T>().FirstOrDefault();
        public static IEnumerable<T> GetAll<T>() where T : Window => All.OfType<T>();
        public static bool IsOpen<T>() where T : Window => Get<T>() != null;
        public static T ShowSingle<T>(Action<T>? setup = null, Action<T>? onExisting = null)
            where T : Window, new()
            => ShowSingle(() => new T(), setup, onExisting);
        public static T ShowSingle<T>(Func<T> create, Action<T>? setup = null, Action<T>? onExisting = null)
            where T : Window
        {
            var existing = Get<T>();
            if (existing != null)
            {
                onExisting?.Invoke(existing);
                return Bring(existing);
            }
            var win = create();
            setup?.Invoke(win);
            win.Show();
            return win;
        }
        public static T ShowSingleKeyed<T>(object key, Func<T> create,
            Action<T>? setup = null, Action<T>? onExisting = null) where T : Window
        {
            var k = (typeof(T), key);
            if (Keyed.TryGetValue(k, out var w) && w is T existing)
            {
                onExisting?.Invoke(existing);
                return Bring(existing);
            }
            var win = create();
            setup?.Invoke(win);
            Keyed[k] = win;
            win.Closed += (_, _) => Keyed.Remove(k);
            win.Show();
            return win;
        }
        public static bool? ShowSingleDialog<T>(Func<T> create, Action<T>? setup = null) where T : Window
        {
            var existing = Get<T>();
            if (existing != null) { Bring(existing); return null; }
            var win = create();
            setup?.Invoke(win);
            return win.ShowDialog();
        }
        public static bool? ShowSingleDialog<T>(Action<T>? setup = null) where T : Window, new()
            => ShowSingleDialog(() => new T(), setup);
        public static T ShowSingleInstance<T>(this Func<T> creator) where T : Window
            => ShowSingle(creator);
        public static void Toggle<T>(Func<T> create, Action<T>? setup = null) where T : Window
        {
            if (!Close<T>()) ShowSingle(create, setup);
        }
        public static T Bring<T>(this T w) where T : Window
        {
            if (!w.IsVisible) w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            return w;
        }
        public static bool Close<T>() where T : Window
        {
            var list = All.OfType<T>().ToList();
            foreach (var w in list) w.Close();
            return list.Count > 0;
        }
        public static void CloseAllExceptMain() => CloseWhere(w => !IsMain(w));
        public static void CloseAllExcept<T>() where T : Window
            => CloseWhere(w => !IsMain(w) && w is not T);
        public static void CloseWhere(Func<Window, bool> filter)
        {
            foreach (var w in All.Where(filter)) w.Close();
        }
        public static void Minimize(this Window w) => w.WindowState = WindowState.Minimized;
        public static void MinimizeAll(bool includeMain = true)
        {
            foreach (var w in All.Where(w => includeMain || !IsMain(w))) w.Minimize();
        }
        public static void RestoreAll()
        {
            foreach (var w in All.Where(w => w.WindowState == WindowState.Minimized))
                w.WindowState = WindowState.Normal;
        }
        public static void CenterOnActive(Window w)
        {
            var owner = Active;
            if (owner == null || ReferenceEquals(owner, w) || !owner.IsLoaded) return;
            w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        private static bool IsMain(Window w) => ReferenceEquals(w, Application.Current?.MainWindow);
    }
}