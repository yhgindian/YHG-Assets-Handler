using CommunityToolkit.Mvvm.ComponentModel;
namespace YHG_Assets_Handler.Models
{
    public enum OptionKind { Choice, Bool, Text, Number }

    /// <summary>Defines an option model consumed by the view layer for dynamic UI generation.</summary>
    public partial class OptionParam : ObservableObject
    {
        public OptionParam(string key, string label, OptionKind kind, object? defaultValue = null,
                           string? description = null)
        {
            Key = key; Label = label; Kind = kind; Description = description;
            DefaultValue = defaultValue;
            Value = defaultValue;
        }

        public string Key { get; }
        public string Label { get; }
        public string? Description { get; }
        public OptionKind Kind { get; }
        public object? DefaultValue { get; }

        /// <summary>List for choices.</summary>
        public IReadOnlyList<object> Choices { get; init; } = Array.Empty<object>();

        /// <summary>Choice (null = ToString).</summary>
        public string? DisplayMemberPath { get; init; }

        public string Placeholder { get; init; } = "";

        /// <summary>null return = OK, string = error message.</summary>
        public Func<object?, string?>? Validator { get; init; }

        [ObservableProperty, NotifyPropertyChangedFor(nameof(BoolValue)), NotifyPropertyChangedFor(nameof(TextValue))]
        private object? value;

        [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))]
        private string error = "";

        public bool HasError => !string.IsNullOrEmpty(Error);

        public bool IsChoice => Kind == OptionKind.Choice;
        public bool IsBool => Kind == OptionKind.Bool;
        public bool IsInput => Kind is OptionKind.Text or OptionKind.Number;

        // Typed bindings (for CheckBox / TextBox)
        public bool? BoolValue
        {
            get => Value is bool b && b;
            set => Value = value == true;
        }

        public string TextValue
        {
            get => Value?.ToString() ?? "";
            set => Value = value;
        }

        partial void OnValueChanged(object? value) => Error = "";

        public void Reset() => Value = DefaultValue;

        /// <summary>Parses a numeric kind string into a double-precision float.</summary>
        public object? GetFinalValue()
        {
            if (Kind != OptionKind.Number) return Kind == OptionKind.Text ? (Value?.ToString() ?? "").Trim() : Value;
            return double.TryParse(Value?.ToString(), out double d) ? d : null;
        }

        /// <summary>true = valid.</summary>
        public bool Validate()
        {
            if (Kind == OptionKind.Number && !double.TryParse(Value?.ToString(), out _))
            { Error = "Enter a valid number."; return false; }

            if (Kind == OptionKind.Choice && Value == null)
            { Error = "Select an option."; return false; }

            string? e = Validator?.Invoke(GetFinalValue());
            Error = e ?? "";
            return e == null;
        }
    }
}