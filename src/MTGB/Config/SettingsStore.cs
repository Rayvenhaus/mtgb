using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;
using System.Text.Json;

namespace MTGB.Config;

public interface ISettingsStore
{
    void Save();
}

public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true };

    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<SettingsStore> _logger;
    private readonly object _lock = new();

    public SettingsStore(
        IOptions<AppSettings> settings,
        ILogger<SettingsStore> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                DataPaths.EnsureDirectoriesExist();

                var tempPath = DataPaths.SettingsFile + ".tmp";
                var json = JsonSerializer.Serialize(
                    _settings.Value,
                    JsonOptions);

                File.WriteAllText(tempPath, json);
                File.Move(
                    tempPath,
                    DataPaths.SettingsFile,
                    overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to save MTGB settings.");
                throw;
            }
        }
    }
}
