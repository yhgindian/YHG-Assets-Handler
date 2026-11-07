using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsHandler.Core.Filtering
{
    public partial class FilterEditorViewModel : ObservableObject
    {
        private readonly Func<FilterRule> _factory;

        public IReadOnlyList<IFilterFieldInfo> Fields { get; }
        public ObservableCollection<FilterRule> Rules { get; } = new();

        [ObservableProperty] private bool _matchAllRules = true;   // true = AND, false = OR

        /// <summary>
        /// Raised on Apply, OK, or Reset actions. Implement filtering logic here.
        /// </summary>
        public event Action<FilterEditorViewModel>? ApplyRequested;
        public event Action<bool?>? CloseRequested;

        public FilterEditorViewModel(IReadOnlyList<IFilterFieldInfo> fields, Func<FilterRule> factory,
                                     IEnumerable<FilterRule>? current = null, bool matchAll = true)
        {
            Fields = fields;
            _factory = factory;
            MatchAllRules = matchAll;

            if (current != null)
                foreach (var r in current) Rules.Add(r.Clone());   // Working copy: prevents mutation on cancel

            if (Rules.Count == 0) AddRule();
        }

        public static FilterEditorViewModel For<T>(IReadOnlyList<IFilterField<T>> fields,
            IEnumerable<FilterRule>? current = null, bool matchAll = true)
            => new(fields.Cast<IFilterFieldInfo>().ToList(), () => new FilterRuleItem<T>(), current, matchAll);

        /// <summary>
        /// Returns a clone of all currently active rules to construct the filter.
        /// </summary>
        public List<FilterRule> GetActiveRules() => Rules.Where(r => r.IsActive).Select(r => r.Clone()).ToList();

        /// <summary>
        /// Returns a clone of all rules to persist state for subsequent editor sessions.
        /// </summary>
        public List<FilterRule> Snapshot() => Rules.Select(r => r.Clone()).ToList();

        [RelayCommand]
        private void AddRule()
        {
            var rule = _factory();
            rule.SelectedField = Fields.FirstOrDefault();
            Rules.Add(rule);
        }

        [RelayCommand]
        private void RemoveRule(FilterRule? rule)
        {
            if (rule == null) return;
            Rules.Remove(rule);
            if (Rules.Count == 0) AddRule();   // Ensure at least one rule entry exists
        }

        [RelayCommand]
        private void Reset()
        {
            Rules.Clear();
            AddRule();
            MatchAllRules = true;
            ApplyRequested?.Invoke(this);      // Trigger filter reset
        }

        [RelayCommand] private void Apply() => ApplyRequested?.Invoke(this);

        [RelayCommand]
        private void Ok()
        {
            ApplyRequested?.Invoke(this);
            CloseRequested?.Invoke(true);
        }

        [RelayCommand] private void Cancel() => CloseRequested?.Invoke(false);
    }
}