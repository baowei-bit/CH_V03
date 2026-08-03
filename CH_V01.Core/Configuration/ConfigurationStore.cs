using System.Text.Json;

namespace CH_V01.Core.Configuration;

public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ConfigurationStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public AppConfiguration Load()
    {
        if (!File.Exists(FilePath))
        {
            var defaultConfiguration = new AppConfiguration();
            defaultConfiguration.TestItems.EnsureDefaultItems();
            Save(defaultConfiguration);
            return defaultConfiguration;
        }

        var json = File.ReadAllText(FilePath);
        var configuration = JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions) ?? new AppConfiguration();
        configuration.User.EnsureDefaultAccounts();
        configuration.TestItems.EnsureDefaultItems();
        return configuration;
    }

    public void Save(AppConfiguration configuration)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(configuration, JsonOptions);
        File.WriteAllText(FilePath, json);
    }

    public static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidOperationException("配置克隆失败");
    }
}
