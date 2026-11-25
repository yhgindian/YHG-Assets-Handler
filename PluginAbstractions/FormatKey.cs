namespace PluginAbstractions;

public readonly record struct FormatKey(string Value)
{
    // Built-in known formats (core ko pehle se pata hain)
    public static readonly FormatKey Unknown = new("unknown");

    public override string ToString() => Value;
}