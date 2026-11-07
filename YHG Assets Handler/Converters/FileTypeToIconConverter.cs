using AssetsHandler.Core;
using MahApps.Metro.IconPacks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace YHG_Assets_Handler.Converters
{
    public class FileTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Models.ExplorerItem item)
            {
                if (item.IsFolder)
                    return PackIconMaterialKind.Folder;

                return item.FileType switch
                {
                    FileType.UnityAsset or FileType.UnityBundle or FileType.UnityWebFile => PackIconMaterialKind.Cube,
                    _ => PackIconMaterialKind.File
                };
            }

            return PackIconMaterialKind.File;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
