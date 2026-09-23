using System.IO;
using System.Text.Json;
using STPipelineGUI.Models;

namespace STPipelineGUI.Services;

public sealed class UserConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<UserConfig> LoadAsync(string path)
    {
        if (!File.Exists(path))
        {
            return new UserConfig();
        }

        var json = await File.ReadAllTextAsync(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new UserConfig();
        }

        return JsonSerializer.Deserialize<UserConfig>(json, JsonOptions) ?? new UserConfig();
    }

    public async Task SaveAsync(string path, UserConfig config)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }
}
