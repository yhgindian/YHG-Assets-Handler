using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PluginAbstractions;
using YHG_Assets_Handler.Plugins;

namespace YHG_Assets_Handler.Converters
{
    public class FormatToColorConverter : IValueConverter
    {
        private static readonly Brush Default = Make("#569CD6");
        private static readonly Brush Folder = Make("#DCB67A");
        private static readonly ConcurrentDictionary<string, Brush> Cache = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not ExplorerItem item) return Default;
            if (item.IsFolder) return Folder;

            var def = PluginManager.Instance.GetFormat(item.Format);
            if (def == null) return Default;

            return Cache.GetOrAdd(def.IconColor, c =>
            {
                try { return Make(c); } catch { return Default; }
            });
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;

        private static Brush Make(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
            b.Freeze();
            return b;
        }
    }
}
