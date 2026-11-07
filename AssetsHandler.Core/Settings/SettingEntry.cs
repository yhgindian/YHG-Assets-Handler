using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text.Json;

public readonly struct SettingEntry
{
    public string Key { get; }
    public string? RawValue { get; }

    [MemberNotNullWhen(false, nameof(RawValue))]
    public bool IsNullOrEmpty => string.IsNullOrWhiteSpace(RawValue);
    public bool Exists => RawValue is not null;

    public SettingEntry(string key, string? rawValue)
    {
        Key = key;
        RawValue = rawValue;
    }

    // ==========================================
    // 1. Primitive Numerics & Booleans
    // ==========================================
    public bool AsBool(bool fallback = false) =>
        bool.TryParse(RawValue, out var v) ? v : fallback;

    public byte AsByte(byte fallback = 0) =>
        byte.TryParse(RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public sbyte AsSByte(sbyte fallback = 0) =>
        sbyte.TryParse(RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public short AsShort(short fallback = 0) =>
        short.TryParse(RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public ushort AsUShort(ushort fallback = 0) =>
        ushort.TryParse(RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public int AsInt(int fallback = 0) =>
        int.TryParse(RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public uint AsUInt(uint fallback = 0) =>
        uint.TryParse(RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public long AsLong(long fallback = 0L) =>
        long.TryParse(RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public ulong AsULong(ulong fallback = 0UL) =>
        ulong.TryParse(RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public float AsFloat(float fallback = 0f) =>
        float.TryParse(RawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public double AsDouble(double fallback = 0d) =>
        double.TryParse(RawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public decimal AsDecimal(decimal fallback = 0m) =>
        decimal.TryParse(RawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    // ==========================================
    // 2. Text, Strings & Characters
    // ==========================================
    public string AsString(string fallback = "") => RawValue ?? fallback;

    public char AsChar(char fallback = '\0') =>
        !string.IsNullOrEmpty(RawValue) ? RawValue[0] : fallback;

    // ==========================================
    // 3. Date, Time & Spans (ISO-8601 & Roundtrip)
    // ==========================================
    public DateTime AsDateTime(DateTime fallback = default) =>
        DateTime.TryParse(RawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var v) ? v : fallback;

    public DateTimeOffset AsDateTimeOffset(DateTimeOffset fallback = default) =>
        DateTimeOffset.TryParse(RawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var v) ? v : fallback;

    public TimeSpan AsTimeSpan(TimeSpan fallback = default) =>
        TimeSpan.TryParse(RawValue, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public DateOnly AsDateOnly(DateOnly fallback = default) =>
        DateOnly.TryParse(RawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : fallback;

    public TimeOnly AsTimeOnly(TimeOnly fallback = default) =>
        TimeOnly.TryParse(RawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : fallback;

    // ==========================================
    // 4. Enums & Identifiers
    // ==========================================
    public TEnum AsEnum<TEnum>(TEnum fallback = default) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(RawValue, ignoreCase: true, out var v) ? v : fallback;

    public Guid AsGuid(Guid fallback = default) =>
        Guid.TryParse(RawValue, out var v) ? v : fallback;

    // ==========================================
    // 5. Web & Network
    // ==========================================
    public Uri? AsUri(UriKind kind = UriKind.RelativeOrAbsolute) =>
        Uri.TryCreate(RawValue, kind, out var uri) ? uri : null;

    public IPAddress? AsIPAddress() =>
        IPAddress.TryParse(RawValue, out var ip) ? ip : null;

    // ==========================================
    // 6. Binary / Hex Bytes
    // ==========================================
    public byte[] AsBytes()
    {
        if (IsNullOrEmpty) return Array.Empty<byte>();
        try { return Convert.FromHexString(RawValue); }
        catch { return Array.Empty<byte>(); }
    }

    // ==========================================
    // 7. Arrays & Lists
    // ==========================================
    public string[] AsArray(char delimiter = ',') =>
        IsNullOrEmpty ? Array.Empty<string>() : RawValue.Split(delimiter, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public int[] AsIntArray(char delimiter = ',')
    {
        var rawParts = AsArray(delimiter);
        var result = new List<int>(rawParts.Length);
        foreach (var p in rawParts)
        {
            if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
                result.Add(val);
        }
        return result.ToArray();
    }

    // ==========================================
    // 8. Complex JSON Objects & Collections
    // ==========================================
    public T? AsJson<T>(JsonSerializerOptions? options = null)
    {
        if (IsNullOrEmpty) return default;
        try { return JsonSerializer.Deserialize<T>(RawValue, options); }
        catch { return default; }
    }

    // ==========================================
    // 9. Generic Conversion Engine (Safe & Dynamic)
    // ==========================================
    public T As<T>(T defaultValue = default!)
    {
        if (IsNullOrEmpty) return defaultValue;

        Type targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (targetType.IsEnum)
        {
            if (Enum.TryParse(targetType, RawValue, ignoreCase: true, out var enumResult))
                return (T)enumResult!;
            return defaultValue;
        }

        try
        {
            return (T)Convert.ChangeType(RawValue, targetType, CultureInfo.InvariantCulture);
        }
        catch
        {
            try
            {
                return JsonSerializer.Deserialize<T>(RawValue) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
    }

    // High-performance IParsable for .NET (zero boxing)
    public T AsParsable<T>(T defaultValue = default!) where T : IParsable<T>
    {
        if (IsNullOrEmpty) return defaultValue;

        return T.TryParse(RawValue, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    // ==========================================
    // 10. Operators & Implicit Type Casting
    // ==========================================
    public static implicit operator SettingEntry(string value) => new(string.Empty, value);
    public static implicit operator string(SettingEntry entry) => entry.AsString();
    public static implicit operator int(SettingEntry entry) => entry.AsInt();
    public static implicit operator float(SettingEntry entry) => entry.AsFloat();
    public static implicit operator double(SettingEntry entry) => entry.AsDouble();
    public static implicit operator bool(SettingEntry entry) => entry.AsBool();
    public static implicit operator Guid(SettingEntry entry) => entry.AsGuid();
}