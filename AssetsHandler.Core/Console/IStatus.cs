public enum StatusType
{
    Info,
    Success,
    Warning,
    Error,
    Command
}

public enum ConsoleSound
{
    None,
    Asterisk,
    Beep,
    Exclamation,
    Hand,
    Question
}

public interface IStatus
{
    void Clear();
    IStatusLine Write(string message, StatusType type = StatusType.Info);
    IStatusLine WriteLink(string prefix, string clickablePath, StatusType type = StatusType.Info);
    void UpdateLast(string message, StatusType type = StatusType.Info);
    void UpdateLastColor(StatusType type);
    Task<string> ReadCommandAsync(string prompt = "> ");
    Task<string?> ReadAsync(string prompt = "> ", ConsoleReadOptions? options = null, CancellationToken ct = default);
    Task<bool> ConfirmAsync(string question, bool defaultYes = false);
}


public sealed class DummyStatus : IStatus
{
    public void Clear() { }
    public IStatusLine Write(string message, StatusType type = StatusType.Info) => NullStatusLine.Instance;
    public IStatusLine WriteLink(string prefix, string path, StatusType type = StatusType.Info) => NullStatusLine.Instance;
    public void UpdateLast(string message, StatusType type = StatusType.Info) { }
    public void UpdateLastColor(StatusType type) { }
    public Task<string> ReadCommandAsync(string prompt = "> ") => Task.FromResult(string.Empty);
    public Task<string?> ReadAsync(string prompt = "> ", ConsoleReadOptions? o = null, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task<bool> ConfirmAsync(string q, bool defaultYes = false) => Task.FromResult(defaultYes);
}