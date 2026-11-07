using AssetsHandler.Core;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using YHG_Assets_Handler.Models;
namespace YHG_Assets_Handler.Converters
{
    public class FileTypeToColorConverter : IValueConverter
    {
        private static readonly Brush Unity = Make("#00E6FF");
        private static readonly Brush Default = Make("#569CD6");
        private static readonly Brush Folder = Make("#DCB67A");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ExplorerItem { IsFolder: true }) return Folder;

            var ft = value is ExplorerItem it ? it.FileType : value as FileType?;
            return ft switch
            {
                FileType.UnityAsset or FileType.UnityBundle or FileType.UnityWebFile => Unity,
                _ => Default
            };
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