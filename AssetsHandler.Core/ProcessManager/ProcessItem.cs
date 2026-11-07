using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows.Input;

namespace AssetsHandler.Core.ProcessManager;

public enum ProcessState { Running, Completed, Cancelled, Failed }

/// <summary>
/// Execution handle provided to background workers to report progress and handle cancellation without UI coupling.
/// </summary>
public interface IProcessHandle
{
    CancellationToken Token { get; }
    bool IsCancellationRequested { get; }
    IProgress<double> Progress { get; }
    void Report(double percent, string? status = null);
    void SetStatus(string status);
    void SetIndeterminate(string? status = null);
    void Complete(string? status = null);
    void Cancelled(string? status = null);
    void Fail(string? error = null);
}

public sealed class ProcessItem : INotifyPropertyChanged, IProcessHandle
{
    private readonly CancellationTokenSource _cts = new();
    private readonly RelayCommand _cancelCmd;
    private IProgress<double>? _progress;
    private string _status = "";
    private double _percent;
    private bool _indeterminate = true;
    private ProcessState _state;
    private int _finished;
    private long _lastTick;

    internal ProcessItem(string title, bool canCancel)
    {
        Title = title;
        CanCancel = canCancel;
        Token = _cts.Token;
        StartedAt = DateTime.Now;
        _cancelCmd = new RelayCommand(Cancel, () => CanCancelNow);
    }

    internal event Action<ProcessItem>? Finished;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }
    public bool CanCancel { get; }
    public DateTime StartedAt { get; }
    public CancellationToken Token { get; }
    public ICommand CancelCommand => _cancelCmd;

    public string Status
    {
        get => _status;
        private set { if (_status != value) { _status = value; Raise(); } }
    }

    public double Percent
    {
        get => _percent;
        private set { _percent = value; Raise(); Raise(nameof(BarValue)); Raise(nameof(PercentText)); }
    }

    public bool IsIndeterminate
    {
        get => _indeterminate;
        private set
        {
            if (_indeterminate == value) return;
            _indeterminate = value;
            Raise(); Raise(nameof(BarValue)); Raise(nameof(PercentText));
        }
    }

    public ProcessState State
    {
        get => _state;
        private set
        {
            _state = value;
            Raise(); Raise(nameof(IsFinished)); Raise(nameof(CanCancelNow)); Raise(nameof(PercentText));
            NotifyCommand();
        }
    }

    public bool IsFinished => Volatile.Read(ref _finished) != 0;
    public bool IsCancellationRequested => Token.IsCancellationRequested;
    public bool CanCancelNow => CanCancel && State == ProcessState.Running && !_cts.IsCancellationRequested;
    public double BarValue => IsIndeterminate ? 100 : Percent;

    public string PercentText => State switch
    {
        ProcessState.Running => IsIndeterminate ? "" : $"{Percent:0}%",
        ProcessState.Completed => "100%",
        _ => ""
    };

    public IProgress<double> Progress => _progress ??= new ReportProgress(this);

    public void Report(double percent, string? status = null)
    {
        if (IsFinished) return;

        // Throttle UI updates to a maximum of ~30 fps
        long now = Environment.TickCount64;
        if (percent < 100 && now - _lastTick < 33) return;
        _lastTick = now;

        percent = double.IsNaN(percent) ? 0 : Math.Clamp(percent, 0, 100);
        if (status != null) Status = status;
        if (IsIndeterminate) IsIndeterminate = false;
        if (Math.Abs(percent - _percent) >= 0.1) Percent = percent;
    }

    public void SetStatus(string status) { if (!IsFinished) Status = status; }

    public void SetIndeterminate(string? status = null)
    {
        if (IsFinished) return;
        if (status != null) Status = status;
        IsIndeterminate = true;
    }

    public void Complete(string? status = null) => TryFinish(ProcessState.Completed, status ?? "Done");
    public void Cancelled(string? status = null) => TryFinish(ProcessState.Cancelled, status ?? "Cancelled");
    public void Fail(string? error = null)
        => TryFinish(ProcessState.Failed, string.IsNullOrWhiteSpace(error) ? "Failed" : "Failed: " + error);

    public void Cancel()
    {
        if (!CanCancel || IsFinished) return;
        try { _cts.Cancel(); } catch (ObjectDisposedException) { return; }

        Status = "Cancelling...";
        Raise(nameof(CanCancelNow));
        NotifyCommand();
    }

    private void TryFinish(ProcessState state, string status)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;

        IsIndeterminate = false;
        if (state == ProcessState.Completed) Percent = 100;
        Status = status;
        State = state;

        _cts.Dispose();
        Finished?.Invoke(this);
    }

    // Command notifications must be dispatched on the UI thread
    private void NotifyCommand() => ProcessManager.Post(_cancelCmd.NotifyCanExecuteChanged);

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class ReportProgress : IProgress<double>
    {
        private readonly ProcessItem _item;
        public ReportProgress(ProcessItem item) => _item = item;
        public void Report(double value) => _item.Report(value);
    }
}