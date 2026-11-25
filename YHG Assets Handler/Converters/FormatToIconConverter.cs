using System.Globalization;
using System.Windows.Data;
using MahApps.Metro.IconPacks;
using PluginAbstractions;
using YHG_Assets_Handler.Plugins;

namespace YHG_Assets_Handler.Converters
{
    /// <summary>Explorer icon from the format registry; the host has no idea which formats exist.</summary>
    public class FormatToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not ExplorerItem item) return PackIconMaterialKind.File;
            if (item.IsFolder) return PackIconMaterialKind.Folder;

            var def = PluginManager.Instance.GetFormat(item.Format);
            if (def != null && Enum.TryParse<PackIconMaterialKind>(def.IconKind, out var kind))
                return kind;

            return PackIconMaterialKind.File;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
