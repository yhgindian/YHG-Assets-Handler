using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PluginAbstractions;

public partial class ExplorerItem : ObservableObject, IDisposable
{
    /// <summary>Set by the host. Resolves the format of a file path (plugins' detectors).</summary>
    public static Func<string, FormatKey>? FormatResolver { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private bool _isFolder;

    public ObservableCollection<ExplorerItem> Children { get; set; } = new();
    public ExplorerItem? Parent = null;

    /// <summary>False for virtual children that live inside a container (e.g. files inside a bundle).</summary>
    public bool CanCloseIndependently { get; set; } = true;

    private FormatKey? _format;
    public FormatKey Format
    {
        get
        {
            if (IsFolder) return FormatKey.Unknown;
            if (_format == null)
                _format = FormatResolver?.Invoke(FullPath) ?? FormatKey.Unknown;
            return _format.Value;
        }
        set
        {
            if (SetProperty(ref _format, value))
                OnPropertyChanged(nameof(Format));
        }
    }

    public BaseViewModel? CachedViewModel { get; set; }

    public T? GetCachedViewModel<T>() where T : class => CachedViewModel as T;

    public void SetCachedViewModel<T>(T viewModel) where T : BaseViewModel => CachedViewModel = viewModel;

    public void ResetFormatCache()
    {
        _format = null;
        OnPropertyChanged(nameof(Format));
    }

    public void Dispose()
    {
        try
        {
            CachedViewModel?.Dispose();
            CachedViewModel = null;

            foreach (var child in Children)
                child.Dispose();
            Children.Clear();
        }
        catch
        {
            // Silent safe dispose
        }
    }
}
