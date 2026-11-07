using AssetsHandler.Core;
using AssetsTools.NET;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using YHG_Assets_Handler.Utils;
using YHG_Assets_Handler.ViewModels;

namespace YHG_Assets_Handler.Models
{
    public partial class ExplorerItem : ObservableObject, IDisposable
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _fullPath = string.Empty;

        [ObservableProperty]
        private bool _isFolder;

        public ObservableCollection<ExplorerItem> Children { get; set; } = new ObservableCollection<ExplorerItem>();
        public ExplorerItem? Parent = null;

        public AssetsFileReader? Reader { get; set; }

        private FileType? _fileType;
        public FileType FileType
        {
            get
            {
                if (IsFolder) return FileType.Unknown;
                if (_fileType == null)
                {
                    _fileType = FileTypeDetector.DetectFileType(FullPath);
                }
                return _fileType.Value;
            }
            set
            {
                if (SetProperty(ref _fileType, value))
                {
                    OnPropertyChanged(nameof(FileType));
                }
            }
        }

        public BaseViewModel? CachedViewModel { get; set; }

        public T? GetCachedViewModel<T>() where T : class
        {
            return CachedViewModel as T;
        }

        public void SetCachedViewModel<T>(T viewModel) where T : BaseViewModel
        {
            CachedViewModel = viewModel;
        }

        public void ResetFileTypeCache()
        {
            _fileType = null;
            OnPropertyChanged(nameof(FileType));
        }

        public void Dispose()
        {
            try
            {
                CachedViewModel?.Dispose();
                CachedViewModel = null;

                Reader?.Close();
                Reader = null;

                foreach (var child in Children)
                {
                    child.Dispose();
                }
                Children.Clear();
            }
            catch
            {
                // Silent safe dispose
            }
        }
    }
}