using System;
using System.IO;
using YHG_Assets_Handler.Utils;

namespace YHG_Assets_Handler.Workspace
{
    public abstract class BaseWorkspace : IDisposable
    {
        public Guid SessionId { get; } = Guid.NewGuid();

        // Root session directory
        public string SessionDirectory { get; }

        // Sub-directories
        public string TempFilesDirectory { get; }
        public string BackupsDirectory { get; }

        public bool IsDisposed { get; private set; }
        public bool IsModified { get; private set; } = false;
        public bool IsSaved { get; private set; } = true;

        protected BaseWorkspace()
        {
            // Returns verified, ready-to-use directory paths directly from the static path resolver.
            (SessionDirectory, TempFilesDirectory, BackupsDirectory) =
                AppPathResolver.ResolveSessionPaths(SessionId);
        }

        public void SetDirty(bool dirty = true)
        {
            IsModified = dirty;
            if (dirty)
            {
                IsSaved = false;
            }
        }

        public void MarkSaved()
        {
            IsSaved = true;
            IsModified = false;
        }

        public bool IsDirty => IsModified && !IsSaved;
        public bool HasModified => IsModified;

        public void Dispose()
        {
            if (IsDisposed) return;

            try
            {
                OnDispose();
            }
            finally
            {
                CleanupTempFilesDirectory();
                IsDisposed = true;
                GC.SuppressFinalize(this);
            }
        }

        private void CleanupTempFilesDirectory()
        {
            try
            {
                if (!string.IsNullOrEmpty(TempFilesDirectory) && Directory.Exists(TempFilesDirectory))
                {
                    Directory.Delete(TempFilesDirectory, recursive: true);
                }
            }
            catch
            {
                
            }
        }

        protected abstract void OnDispose();
    }
}