using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;

namespace AssetsHandler.Core
{
    public static class DllLoader
    {
        private static readonly ConcurrentDictionary<string, IntPtr> LoadedHandles = new(StringComparer.OrdinalIgnoreCase);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

        // Adds the DLL's directory to the search path for resolving its dependencies
        private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

        public static IntPtr Load(string dllName, DllDir dllDir = DllDir.RDM)
        {
            if (string.IsNullOrWhiteSpace(dllName))
                return IntPtr.Zero;

            if (!dllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                dllName += ".dll";
            }

            if (dllDir == DllDir.System)
            {
                return GetOrLoad(dllName, () => LoadLibraryEx(dllName, IntPtr.Zero, 0));
            }

            string targetDir = GetTargetDirectory(dllDir);
            string fullPath = Path.Combine(targetDir, dllName);

            if (!File.Exists(fullPath))
            {
                return IntPtr.Zero;
            }

            // Ensures the directory containing the native library is searched first for dependent binaries
            return GetOrLoad(fullPath, () => LoadLibraryEx(fullPath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH));
        }

        private static string GetTargetDirectory(DllDir dllDir)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            return dllDir switch
            {
                DllDir.RDM => Path.Combine(baseDir, "runtimes", "win-x64", "native"),
                DllDir.Root => baseDir,
                DllDir.System => Environment.SystemDirectory,
                _ => baseDir
            };
        }

        private static IntPtr GetOrLoad(string key, Func<IntPtr> loadAction)
        {
            return LoadedHandles.GetOrAdd(key, _ => loadAction());
        }
    }

    public enum DllDir
    {
        RDM,    // runtimes\win-x64\native
        System, // System32 directory
        Root    // Application base directory
    }
}