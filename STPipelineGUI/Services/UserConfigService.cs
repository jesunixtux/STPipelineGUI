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

        var config = JsonSerializer.Deserialize<UserConfig>(json, JsonOptions) ?? new UserConfig();
        config.Steamworks ??= new SteamworksConfig();
        config.Steamworks.PublisherApiKey ??= string.Empty;
        config.Steamworks.PartnerApiBaseUrl ??= "https://partner.steam-api.com";
        return config;
    }

    public async Task SaveAsync(string path, UserConfig config)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        config.Steamworks ??= new SteamworksConfig();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await WriteAtomicallyAsync(path, json);
    }

    private static async Task WriteAtomicallyAsync(string path, string content)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
