using System.IO;

namespace MTGB.Config;

/// <summary>
/// Centralised path management for all MTGB data files.
/// All paths are relative to the installation directory's Data folder.
/// </summary>
public static class DataPaths
{
    /// <summary>
    /// The base Data directory, located alongside the executable.
    /// </summary>
    public static string BaseDataPath =>
        Path.Combine(AppContext.BaseDirectory, "Data");

    /// <summary>
    /// Path to the user's appsettings.json file.
    /// </summary>
    public static string SettingsFile =>
        Path.Combine(BaseDataPath, "appsettings.json");

    /// <summary>
    /// Path to the notification history file.
    /// </summary>
    public static string HistoryFile =>
        Path.Combine(BaseDataPath, "history.json");

    /// <summary>
    /// Path to the logs directory.
    /// </summary>
    public static string LogsDirectory =>
        Path.Combine(BaseDataPath, "logs");

    /// <summary>
    /// Path to the dumps directory (for API debug dumps).
    /// </summary>
    public static string DumpsDirectory =>
        Path.Combine(BaseDataPath, "dumps");

    /// <summary>
    /// Path to the log file pattern (for logging provider).
    /// </summary>
    public static string LogFilePattern =>
        Path.Combine(LogsDirectory, "mtgb-.log");

    /// <summary>
    /// Path to the built-in appsettings.json in the install directory.
    /// </summary>
    public static string BuiltInSettingsFile =>
        Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    /// <summary>
    /// Ensures the Data directory and all subdirectories exist.
    /// Call this at application startup.
    /// </summary>
    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(BaseDataPath);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(DumpsDirectory);
    }

    /// <summary>
    /// Initialises the user settings file by copying from the built-in template
    /// if it doesn't already exist.
    /// </summary>
    public static void InitialiseSettingsFile()
    {
        if (!File.Exists(SettingsFile) && File.Exists(BuiltInSettingsFile))
        {
            File.Copy(BuiltInSettingsFile, SettingsFile);
        }
    }
}

