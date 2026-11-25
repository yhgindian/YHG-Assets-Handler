using CommunityToolkit.Mvvm.ComponentModel;

namespace PluginAbstractions;

public abstract partial class BaseViewModel : ObservableObject, IDisposable
{
    protected ExplorerItem item;

    [ObservableProperty]
    private int _selectedCounts = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private bool _isModified = false;

    public virtual string DisplayName => IsModified ? $"{item.Name}*" : item.Name;
    public string ItemFullPath => item.FullPath;

    protected BaseViewModel(ExplorerItem item)
    {
        this.item = item;
    }

    /// <summary>Called by the host every time this view model's item gets selected (lazy-load hook).</summary>
    public virtual void OnActivated() { }

    public virtual Task<bool> CanCloseAsync()
    {
        if (!IsModified)
            return Task.FromResult(true);

        bool discard = Dialogs.Current.Confirm(
            "Unsaved Changes",
            $"'{DisplayName}' has unsaved changes.\n\nDo you want to discard changes and close it?");

        return Task.FromResult(discard);
    }

    /// <summary>Cleanup memory, streams, readers and event subscriptions.</summary>
    public virtual void OnClose() { }

    public virtual Task<bool> OnSaveAsync(bool saveAs = false) => Task.FromResult(true);

    public void Dispose()
    {
        OnClose();
        GC.SuppressFinalize(this);
    }
}
