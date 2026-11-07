using System;
using System.Collections.Generic;
using System.Text;

public sealed class ConsoleReadOptions
{
    public bool IsPassword { get; set; }
    public int MaxLength { get; set; }                       // 0 = unlimited
    public string DefaultText { get; set; } = "";
    public bool AddToHistory { get; set; } = true;
    public Func<char, bool>? CharFilter { get; set; }        // e.g. char.IsDigit
    public Func<string, string?>? Validator { get; set; }    // null = ok, string = error message
}