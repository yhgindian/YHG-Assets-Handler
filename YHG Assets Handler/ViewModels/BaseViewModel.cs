using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading.Tasks;
using YHG_Assets_Handler.Models;
using YHG_Assets_Handler.Views;

namespace YHG_Assets_Handler.ViewModels
{
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
        public virtual async Task<bool> CanCloseAsync()
        {
            if (!IsModified)
                return true;

            bool discard = ConfirmationDialog.Show(
                "Unsaved Changes",
                $"'{DisplayName}' has unsaved changes.\n\nDo you want to discard changes and close it?"
            );

            return await Task.FromResult(discard);
        }

        /// <summary>
        /// Cleanup memory, streams, readers and event subscriptions.
        /// </summary>
        public virtual void OnClose()
        {
        }

        /// <summary>
        /// Production Async Save Handler with failure reporting
        /// </summary>
        public virtual async Task<bool> OnSaveAsync(bool saveAs = false)
        {
            return await Task.FromResult(true);
        }

        public void Dispose()
        {
            OnClose();
            GC.SuppressFinalize(this);
        }
    }
}