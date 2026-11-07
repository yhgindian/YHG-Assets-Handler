using AssetsHandler.Controls;
using AssetsTools.NET.Extra;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using YHG_Assets_Handler.Workspace.UnityAssets;

namespace YHG_Assets_Handler.Utils.Unity
{
    public static class AssetContext
    {
        public static void ShowOriginalFile(AssetContainer assetContainer)
        {
            try
            {
                string? filePath = GetFilePath(assetContainer);
                if (string.IsNullOrEmpty(filePath))
                {
                    MessageBox.Show("File path is null or empty.");
                    return;
                }

                var args = $"/select, \"{filePath}\"";
                using (Process explorerProcess = new Process())
                {
                    explorerProcess.StartInfo = new ProcessStartInfo("explorer.exe", args)
                    {
                        UseShellExecute = true
                    };
                    explorerProcess.Start();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening file in Explorer: {ex.Message}");
            }
        }

        public static string? GetFilePath(AssetContainer cont)
        {
            return GetFilePath(cont.FileInstance);
        }

        public static string? GetFilePath(AssetsFileInstance instance)
        {
            if (instance == null) return null;

            string path = instance.path;
            if (!string.IsNullOrEmpty(instance.parentBundle?.path))
            {
                return instance.parentBundle.path;
            }

            return path;
        }

        public static string? GetDependencyPath(AssetContainer cont, int depID)
        {
            return GetDependencyPath(cont.FileInstance, depID);
        }
        public static string? GetDependencyPath(AssetsFileInstance inst, int depID)
        {
            var idx = depID - 1;
            var deps = inst.file.Metadata.Externals;
            if ((idx + 1) > deps.Count)
            {
                NotificationManager.Error("Dependency doesn't exist");
                return null;
            }

            var dep = deps[idx];
            var depName = dep.PathName;

            string? filePath = null;
            var path = GetFilePath(inst);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                var combined = Path.Combine(dir, depName);
                if (File.Exists(combined))
                {
                    filePath = combined;
                }
            }

            if (string.IsNullOrEmpty(filePath))
            {
                OpenFileDialog dlg = new OpenFileDialog()
                {
                    Multiselect = false,
                    FileName = depName,
                };
                if (dlg.ShowDialog(WindowHelper.Main) == true)
                filePath = dlg.FileName;
            }

            return filePath;
        }

        public static AssetsFileInstance? GetDepLoad(UnityAssetWorkspace w, AssetContainer cont, int depID)
        {
            return GetDepLoad(w, cont.FileInstance, depID);
        }

        public static AssetsFileInstance? GetDepLoad(UnityAssetWorkspace w, AssetsFileInstance inst, int depid)
        {
            if (depid == 0) return inst;
            AssetsFileInstance? instance = null;
            if (inst.parentBundle == null)
            {
                instance = inst.GetDependency(w.am, depid - 1);
                if (instance != null)
                    w.LoadAssetsFile(instance, false);
                else
                    if (GetDependencyPath(inst, depid) is string path && File.Exists(path))
                        instance = w.LoadAssetsFile(path, false);
            }
            else
            {
                var depName = inst.file.Metadata.Externals[depid - 1].PathName;

                if (inst.parentBundle.file.BlockAndDirInfo.DirectoryInfos
                    .Any(di => di.Name == depName))
                {
                    instance = w.am.LoadAssetsFileFromBundle(inst.parentBundle, depName);
                }
            }
            return instance;
        }
    }
}
