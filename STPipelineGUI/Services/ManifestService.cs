using System.IO;
using System.Text.Json;
using STPipelineGUI.Models;

namespace STPipelineGUI.Services;

public sealed class ManifestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<ManifestFile> LoadAsync(string path)
    {
        if (!File.Exists(path))
        {
            return new ManifestFile();
        }

        var json = await File.ReadAllTextAsync(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ManifestFile();
        }

        return JsonSerializer.Deserialize<ManifestFile>(json, JsonOptions) ?? new ManifestFile();
    }

    public async Task SaveAsync(string path, ManifestFile manifest)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }
}
