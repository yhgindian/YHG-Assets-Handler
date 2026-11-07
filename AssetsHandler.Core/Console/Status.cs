public static class Status
{
    public static IStatus Default = new DummyStatus();

    public static void Clear() => Default.Clear();
    public static IStatusLine Write(string message, StatusType type = StatusType.Info) => Default.Write(message, type);
    public static IStatusLine WriteLink(string prefix, string path, StatusType type = StatusType.Info) => Default.WriteLink(prefix, path, type);
    public static void UpdateLast(string message, StatusType type = StatusType.Info) => Default.UpdateLast(message, type);
    public static void UpdateLastColor(StatusType type) => Default.UpdateLastColor(type);
    public static Task<string> ReadCommandAsync(string prompt = "> ") => Default.ReadCommandAsync(prompt);
    public static Task<string?> ReadAsync(string prompt = "> ", ConsoleReadOptions? o = null, CancellationToken ct = default) => Default.ReadAsync(prompt, o, ct);
    public static Task<bool> ConfirmAsync(string q, bool defaultYes = false) => Default.ConfirmAsync(q, defaultYes);
}