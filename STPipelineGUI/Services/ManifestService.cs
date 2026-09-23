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
}
