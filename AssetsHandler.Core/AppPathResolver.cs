using System;
using System.IO;
using System.Reflection;

public static class AppPathResolver
{
    public static string AppName { get; }

    public static string AppDataRoot { get; }
    public static string UserProfileRoot { get; }

    public static string AppLocalDataDir { get; }
    public static string AppUserProfileDir { get; }
    public static string WorkflowDir { get; }

    public static string SessionsDir { get; }
    public static string SettingsFile { get; }

    static AppPathResolver()
    {
        AppName = Assembly.GetEntryAssembly()?.GetName().Name
                  ?? AppDomain.CurrentDomain.FriendlyName;

        AppDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        UserProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        AppLocalDataDir = Path.Combine(AppDataRoot, AppName);
        AppUserProfileDir = Path.Combine(UserProfileRoot, $".{AppName.ToLowerInvariant()}");
        WorkflowDir = Path.Combine(AppUserProfileDir, "workflow");

        SessionsDir = Path.Combine(AppLocalDataDir, "Sessions");
        SettingsFile = Path.Combine(AppLocalDataDir, "settings.bin");

        try
        {
            EnsureDirectory(AppLocalDataDir);
            EnsureDirectory(SessionsDir);
        }
        catch
        {
            // Suppress IO/permission exceptions during static initialization
        }
    }

    /// <summary>
    /// Creates and returns isolated directories (root, temporary files, and backups) for a specified session.
    /// </summary>
    public static (string SessionDir, string TempDir, string BackupsDir) ResolveSessionPaths(Guid sessionId)
    {
        string sessionDir = Path.Combine(SessionsDir, sessionId.ToString("N"));
        string tempDir = Path.Combine(sessionDir, "TempFiles");
        string backupsDir = Path.Combine(sessionDir, "Backups");

        EnsureDirectory(tempDir);
        EnsureDirectory(backupsDir);

        return (sessionDir, tempDir, backupsDir);
    }

    /// <summary>
    /// Ensures that the specified directory exists on disk, creating it if necessary.
    /// </summary>
    public static string EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }
}