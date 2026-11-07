namespace AssetsHandler.Core.Filtering
{
    public enum FilterDataType { String, Number, Boolean, Enum }

    public enum FilterOperator
    {
        Equals, NotEquals, Contains, NotContains, StartsWith, EndsWith,
        GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual,
        In, NotIn
    }

    public interface IFilterFieldInfo
    {
        string DisplayName { get; }
        string PropertyKey { get; }
        FilterDataType DataType { get; }
        IReadOnlyList<FilterOperator> SupportedOperators { get; }
    }

    public interface IFilterField<TItem> : IFilterFieldInfo
    {
        object? GetValue(TItem item);
    }
}