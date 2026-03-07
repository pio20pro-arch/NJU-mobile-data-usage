using System.Text.Json;
using NjuTrayApp.Models;

namespace NjuTrayApp.Services;

public sealed class ConfigService
{
    private const string DefaultApiKey = "3a44c3d8-d4cd-11eb-b8bc-0242ac130003";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly FileLogger _logger;
    private readonly string _configPath;

    public ConfigService(FileLogger logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var configDirectory = Path.Combine(appData, "NjuTrayApp");
        Directory.CreateDirectory(configDirectory);
        _configPath = Path.Combine(configDirectory, "config.json");
    }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                var config = new AppConfig { ApiKey = DefaultApiKey };
                Save(config);
                return config;
            }

            var json = File.ReadAllText(_configPath);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (loaded is null)
            {
                return new AppConfig();
            }

            return loaded;
        }
        catch (Exception ex)
        {
            _logger.Error($"Config load failed: {ex}");
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            _logger.Error($"Config save failed: {ex}");
        }
    }
}
