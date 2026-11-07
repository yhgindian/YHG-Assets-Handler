using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace AssetsHandler.Core.Filtering
{
    /// <summary>
    /// Non-generic base class for filter rules used for UI data-binding.
    /// </summary>
    public abstract partial class FilterRule : ObservableObject
    {
        [ObservableProperty] private bool _isEnabled = true;

        [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBoolean))]
        private IFilterFieldInfo? _selectedField;

        [ObservableProperty] private FilterOperator _selectedOperator;
        [ObservableProperty] private string _value = string.Empty;
        [ObservableProperty] private IReadOnlyList<FilterOperator> _availableOperators = Array.Empty<FilterOperator>();

        public bool IsBoolean => SelectedField?.DataType == FilterDataType.Boolean;
        public IReadOnlyList<string> BoolValues { get; } = new[] { "true", "false" };

        public bool IsActive => IsEnabled && SelectedField != null && (IsBoolean || !string.IsNullOrWhiteSpace(Value));

        partial void OnSelectedFieldChanged(IFilterFieldInfo? value)
        {
            AvailableOperators = value?.SupportedOperators ?? Array.Empty<FilterOperator>();
            if (value != null) SelectedOperator = AvailableOperators.FirstOrDefault();
            if (value?.DataType == FilterDataType.Boolean && !bool.TryParse(Value, out _)) Value = "true";
        }

        public abstract FilterRule Clone();
    }

    public sealed class FilterRuleItem<TItem> : FilterRule
    {
        public bool IsMatch(TItem item)
        {
            if (!IsActive || SelectedField is not IFilterField<TItem> f) return true;

            object? raw = f.GetValue(item);
            if (raw == null) return false;

            return f.DataType switch
            {
                FilterDataType.Number => EvaluateNumber(raw, Value, SelectedOperator),
                FilterDataType.Boolean => EvaluateBoolean(raw, Value, SelectedOperator),
                _ => EvaluateString(raw.ToString() ?? "", Value, SelectedOperator)
            };
        }

        private static string[] SplitList(string s) =>
            s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public override FilterRule Clone() => new FilterRuleItem<TItem>
        {
            IsEnabled = IsEnabled,
            SelectedField = SelectedField,
            SelectedOperator = SelectedOperator,
            Value = Value
        };

        private static bool EvaluateNumber(object raw, string target, FilterOperator op)
        {
            if (!double.TryParse(raw.ToString(), out double a)) return false;

            if (op is FilterOperator.In or FilterOperator.NotIn)
            {
                bool found = SplitList(target).Any(t => double.TryParse(t, out double e) && a == e);
                return op == FilterOperator.In ? found : !found;
            }

            if (!double.TryParse(target, out double ex)) return false;
            return op switch
            {
                FilterOperator.Equals => a == ex,
                FilterOperator.NotEquals => a != ex,
                FilterOperator.GreaterThan => a > ex,
                FilterOperator.GreaterThanOrEqual => a >= ex,
                FilterOperator.LessThan => a < ex,
                FilterOperator.LessThanOrEqual => a <= ex,
                _ => false
            };
        }

        private static bool EvaluateBoolean(object raw, string target, FilterOperator op)
        {
            if (!bool.TryParse(raw.ToString(), out bool a) || !bool.TryParse(target, out bool e)) return false;
            return op == FilterOperator.Equals ? a == e : a != e;
        }

        private static bool EvaluateString(string a, string t, FilterOperator op) => op switch
        {
            FilterOperator.Contains => a.Contains(t, StringComparison.OrdinalIgnoreCase),
            FilterOperator.NotContains => !a.Contains(t, StringComparison.OrdinalIgnoreCase),
            FilterOperator.Equals => string.Equals(a, t, StringComparison.OrdinalIgnoreCase),
            FilterOperator.NotEquals => !string.Equals(a, t, StringComparison.OrdinalIgnoreCase),
            FilterOperator.StartsWith => a.StartsWith(t, StringComparison.OrdinalIgnoreCase),
            FilterOperator.EndsWith => a.EndsWith(t, StringComparison.OrdinalIgnoreCase),
            FilterOperator.In => SplitList(t).Any(x => string.Equals(a, x, StringComparison.OrdinalIgnoreCase)),
            FilterOperator.NotIn => !SplitList(t).Any(x => string.Equals(a, x, StringComparison.OrdinalIgnoreCase)),
            _ => true
        };
    }

    public static class FilterEngine
    {
        /// <summary>
        /// Builds an executable predicate filter from rules. Returns null if no active rules exist.
        /// </summary>
        public static Predicate<T>? Build<T>(IEnumerable<FilterRule> rules, bool matchAll)
        {
            var active = rules.OfType<FilterRuleItem<T>>().Where(r => r.IsActive).ToArray();
            if (active.Length == 0) return null;
            return matchAll ? x => active.All(r => r.IsMatch(x))
                            : x => active.Any(r => r.IsMatch(x));
        }
    }
}