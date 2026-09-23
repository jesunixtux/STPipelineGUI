using System.IO;
using STPipelineGUI.Models;

namespace STPipelineGUI.Services;

public sealed class OutputFolderService
{
    private readonly PathService _paths;

    public OutputFolderService(PathService paths)
    {
        _paths = paths;
    }

    public IReadOnlyList<string> EnsureSelectedPlatformFolders(SteamProject project)
    {
        var created = new List<string>();
        var appRoot = Path.Combine(_paths.RootPath, "output", project.AppId);
        Directory.CreateDirectory(appRoot);

        foreach (var platform in project.Platforms.SelectedPlatforms())
        {
            var depotPath = project.Depots.FirstOrDefault(d => d.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase))?.ContentRoot;
            var platformPath = string.IsNullOrWhiteSpace(depotPath) ? Path.Combine(appRoot, platform) : _paths.Resolve(depotPath);
            Directory.CreateDirectory(platformPath);
            created.Add(platformPath);
        }

        return created;
    }
}
