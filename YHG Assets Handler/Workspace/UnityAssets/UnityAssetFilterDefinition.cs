using AssetsHandler.Core.Filtering;
using System;
using System.Collections.Generic;
using System.Text;

namespace YHG_Assets_Handler.Workspace.UnityAssets
{
    public static class UnityAssetFilterDefinition
    {
        public static List<IFilterField<UnityAssetObjectModel>> Fields { get; } = new()
        {
            new FilterField<UnityAssetObjectModel, string>(
                "Name", "Asset Name", FilterDataType.String, x => x.Name ?? string.Empty),

            new FilterField<UnityAssetObjectModel, string>(
                "Type", "Type Class", FilterDataType.String, x => x.Type ?? string.Empty),

            new FilterField<UnityAssetObjectModel, string>(
                "Container", "Container Path", FilterDataType.String, x => x.Container ?? string.Empty),

            new FilterField<UnityAssetObjectModel, long>(
                "PathID", "Path ID", FilterDataType.Number, x => x.PathID),

            new FilterField<UnityAssetObjectModel, long>(
                "Size", "Byte Size", FilterDataType.Number, x => x.Size),

            new FilterField<UnityAssetObjectModel, bool>(
                "Modified", "Is Modified", FilterDataType.Boolean, x => !string.IsNullOrEmpty(x.Modified))
        };
    }
}
