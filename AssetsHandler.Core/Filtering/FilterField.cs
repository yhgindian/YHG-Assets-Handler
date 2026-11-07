using System;
using System.Collections.Generic;
using System.Text;

namespace AssetsHandler.Core.Filtering
{
    public class FilterField<TItem, TValue> : IFilterField<TItem>
    {
        public string DisplayName { get; }
        public string PropertyKey { get; }
        public FilterDataType DataType { get; }
        public IReadOnlyList<FilterOperator> SupportedOperators { get; }

        private readonly Func<TItem, TValue> _valueGetter;

        public FilterField(string propertyKey, string displayName, FilterDataType dataType, Func<TItem, TValue> valueGetter)
        {
            PropertyKey = propertyKey;
            DisplayName = displayName;
            DataType = dataType;
            _valueGetter = valueGetter;
            SupportedOperators = ResolveOperators(dataType);
        }

        public object? GetValue(TItem item) => _valueGetter(item);

        private static IReadOnlyList<FilterOperator> ResolveOperators(FilterDataType type) => type switch
        {
            FilterDataType.Number => new[]
            {
                FilterOperator.Equals, FilterOperator.NotEquals,
                FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual,
                FilterOperator.LessThan, FilterOperator.LessThanOrEqual,
                FilterOperator.In, FilterOperator.NotIn
            },
            FilterDataType.Boolean => new[]
            {
                FilterOperator.Equals, FilterOperator.NotEquals
            },
            _ => new[] // String / Enum
            {
                FilterOperator.Contains, FilterOperator.NotContains,
                FilterOperator.Equals, FilterOperator.NotEquals,
                FilterOperator.StartsWith, FilterOperator.EndsWith,
                FilterOperator.In, FilterOperator.NotIn
            }
        };

        public override string ToString() => DisplayName;
    }
}
