using AssetsHandler.Core.ProcessManager;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace AssetsHandler.Core.ProcessManager;

public static class ProcessManager
{
    private static readonly ConcurrentDictionary<ProcessItem, byte> _active = new();
    public static IProcessHost? Host { get; set; }
    public static ObservableCollection<ProcessItem> Processes { get; } = new();
    public static bool AutoShow { get; set; } = false;
    public static bool AutoHide { get; set; } = false;
    public static int ShowDelayMs { get; set; } = 400;
    public static int FinishedLingerMs { get; set; } = 2500;
    public static int ActiveCount => _active.Count;
    public static event Action? ActiveChanged;
    public static event Action<ProcessItem>? ProcessFinished;
    internal static void Post(Action a)
    {
        var h = Host;
        if (h == null) a(); else h.Post(a);
    }
    public static ProcessItem Start(string title, bool canCancel = true)
    {
        var item = new ProcessItem(title, canCancel);
        item.Finished += OnFinished;
        _active[item] = 0;
        ActiveChanged?.Invoke();
        Post(() => Processes.Add(item));
        if (AutoShow)
        {
            Task.Delay(ShowDelayMs).ContinueWith(
                _ => Post(() => { if (!item.IsFinished) Host?.Show(auto: true); }),
                TaskScheduler.Default);
        }
        return item;
    }
    public static async Task<bool> RunAsync(string title, Func<IProcessHandle, Task> work, bool canCancel = true)
    {
        var item = Start(title, canCancel);
        try
        {
            await work(item).ConfigureAwait(false);
            item.Complete();
            return true;
        }
        catch (OperationCanceledException) { item.Cancelled(); return false; }
        catch (Exception ex) { item.Fail(ex.Message); return false; }
    }
    public static Task<bool> RunBackground(string title, Action<IProcessHandle> work, bool canCancel = true)
        => RunAsync(title, h => Task.Run(() => work(h)), canCancel);
    public static void CancelAll()
    {
        foreach (var p in _active.Keys) p.Cancel();
    }
    public static void Show() => Post(() => Host?.Show(auto: false));
    public static void Hide() => Post(() => Host?.Hide(onlyIfAuto: false));
    public static void Toggle() => Post(() => Host?.Toggle());
    private static void OnFinished(ProcessItem item)
    {
        _active.TryRemove(item, out _);
        ActiveChanged?.Invoke();
        ProcessFinished?.Invoke(item);
        int linger = item.State == ProcessState.Failed ? FinishedLingerMs * 3 : FinishedLingerMs;
        Task.Delay(linger).ContinueWith(_ => Post(() =>
        {
            Processes.Remove(item);
            if (Processes.Count == 0 && AutoHide) Host?.Hide(onlyIfAuto: true);
        }), TaskScheduler.Default);
    }
}