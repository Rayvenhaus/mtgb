using System.IO;

namespace MTGB.Config;

public static class DataPaths
{
    public static string InstallPath => AppContext.BaseDirectory;
    public static string DataPath => Path.Combine(InstallPath, "data");
    public static string LogsPath => Path.Combine(InstallPath, "logs");
    public static string AssetsPath => Path.Combine(DataPath, "assets");
    public static string SettingsFile => Path.Combine(DataPath, "appsettings.json");
    public static string HistoryFile => Path.Combine(DataPath, "history.json");
    public static string LogFilePattern => Path.Combine(LogsPath, "mtgb-{Date}.log");
    public static string CurrentLogFile => Path.Combine(
        LogsPath,
        $"mtgb-{DateTime.Now:yyyyMMdd}.log");

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(AssetsPath);
    }
}
