using System.IO;
using System.Reflection;

namespace AssetsHandler.Plugin.Unity
{
    /// <summary>Where this plugin's own files (classdata.tpk, native libs) live.</summary>
    public static class UnityPluginPaths
    {
        public static string PluginDir { get; } =
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;

        public static string ClassData => Path.Combine(PluginDir, "classdata.tpk");
        public static string NativeDir => Path.Combine(PluginDir, "runtimes", "win-x64", "native");
    }
}
