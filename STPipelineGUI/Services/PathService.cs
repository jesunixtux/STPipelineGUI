using System.IO;

namespace STPipelineGUI.Services;

public sealed class PathService
{
    public PathService()
    {
        RootPath = FindRootPath();
    }

    public string RootPath { get; }
    public string ManifestPath => Path.Combine(RootPath, "json", "manifest.json");
    public string UserConfigPath => Path.Combine(RootPath, "config", "user.json");

    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return RootPath;
        }

        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(RootPath, path));
    }

    private static string FindRootPath()
    {
        var current = AppContext.BaseDirectory;

        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(Path.Combine(current, "json")) &&
                Directory.Exists(Path.Combine(current, "config")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
