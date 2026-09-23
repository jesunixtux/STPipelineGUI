using System.IO;
using System.Text;
using STPipelineGUI.Models;

namespace STPipelineGUI.Services;

public sealed class VdfGenerator
{
    private readonly PathService _paths;

    public VdfGenerator(PathService paths)
    {
        _paths = paths;
    }

    public IReadOnlyList<string> Generate(SteamProject project)
    {
        var outputRoot = GetVdfOutputRoot(project);
        Directory.CreateDirectory(outputRoot);

        var depotFiles = new List<string>();
        foreach (var depot in GetSelectedDepots(project))
        {
            var depotPath = Path.Combine(outputRoot, $"depot_{depot.DepotId}.vdf");
            File.WriteAllText(depotPath, BuildDepotVdf(depot));
            depotFiles.Add(depotPath);
        }

        var appBuildPath = Path.Combine(outputRoot, $"app_build_{project.AppId}.vdf");
        File.WriteAllText(appBuildPath, BuildAppVdf(project, depotFiles));

        return depotFiles.Prepend(appBuildPath).ToList();
    }

    public string Preview(SteamProject project)
    {
        var selectedDepots = GetSelectedDepots(project).ToList();
        var previewDepotFiles = selectedDepots
            .Select(d => Path.Combine(GetVdfOutputRoot(project), $"depot_{d.DepotId}.vdf"))
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine(BuildAppVdf(project, previewDepotFiles));

        foreach (var depot in selectedDepots)
        {
            builder.AppendLine();
            builder.AppendLine(BuildDepotVdf(depot));
        }

        return builder.ToString();
    }

    public IReadOnlyList<string> Validate(SteamProject project)
    {
        var errors = new List<string>();

        if (!ulong.TryParse(project.AppId, out _))
        {
            errors.Add("AppID invalido o vacio.");
        }

        var selectedDepots = GetSelectedDepots(project).ToList();
        if (selectedDepots.Count == 0)
        {
            errors.Add("Selecciona al menos una plataforma.");
        }

        foreach (var depot in selectedDepots)
        {
            if (!ulong.TryParse(depot.DepotId, out _))
            {
                errors.Add($"DepotID invalido para {depot.Platform}: {depot.DepotId}");
            }

            var contentRoot = _paths.Resolve(depot.ContentRoot);
            if (!Directory.Exists(contentRoot))
            {
                errors.Add($"No existe la carpeta de contenido para {depot.Platform}: {contentRoot}");
                continue;
            }

            if (!Directory.EnumerateFileSystemEntries(contentRoot).Any())
            {
                errors.Add($"La carpeta de contenido esta vacia para {depot.Platform}: {contentRoot}");
            }
        }

        return errors;
    }

    public string GetAppBuildPath(SteamProject project) => Path.Combine(GetVdfOutputRoot(project), $"app_build_{project.AppId}.vdf");

    private string GetVdfOutputRoot(SteamProject project) => _paths.Resolve(string.IsNullOrWhiteSpace(project.VdfOutput) ? "vdf/generated" : project.VdfOutput);

    private IEnumerable<DepotDefinition> GetSelectedDepots(SteamProject project)
    {
        return project.Depots.Where(d => IsPlatformSelected(project, d.Platform));
    }

    private static bool IsPlatformSelected(SteamProject project, string platform)
    {
        return platform.ToLowerInvariant() switch
        {
            "windows" => project.Platforms.Windows,
            "linux" => project.Platforms.Linux,
            "macos" => project.Platforms.Macos,
            _ => false
        };
    }

    private string BuildAppVdf(SteamProject project, IReadOnlyList<string> depotFiles)
    {
        var buildCache = string.IsNullOrWhiteSpace(project.BuildOutput)
            ? Path.Combine(_paths.RootPath, "steampipe", "cache", project.AppId)
            : _paths.Resolve(project.BuildOutput);

        var contentRoot = string.IsNullOrWhiteSpace(project.ContentRoot)
            ? Path.Combine(_paths.RootPath, "output", project.AppId)
            : _paths.Resolve(project.ContentRoot);

        var builder = new StringBuilder();
        builder.AppendLine("\"appbuild\"");
        builder.AppendLine("{");
        builder.AppendLine($"    \"appid\" \"{Escape(project.AppId)}\"");
        builder.AppendLine($"    \"desc\" \"{Escape(project.Description)}\"");
        builder.AppendLine($"    \"buildoutput\" \"{Escape(buildCache)}\"");
        builder.AppendLine($"    \"contentroot\" \"{Escape(contentRoot)}\"");

        if (!string.Equals(project.Branch, "default", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine($"    \"setlive\" \"{Escape(project.Branch)}\"");
        }

        builder.AppendLine("    \"depots\"");
        builder.AppendLine("    {");

        foreach (var depotFile in depotFiles)
        {
            var depotId = Path.GetFileNameWithoutExtension(depotFile).Replace("depot_", string.Empty, StringComparison.OrdinalIgnoreCase);
            builder.AppendLine($"        \"{Escape(depotId)}\" \"{Escape(depotFile)}\"");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private string BuildDepotVdf(DepotDefinition depot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("\"DepotBuildConfig\"");
        builder.AppendLine("{");
        builder.AppendLine($"    \"DepotID\" \"{Escape(depot.DepotId)}\"");
        builder.AppendLine($"    \"ContentRoot\" \"{Escape(_paths.Resolve(depot.ContentRoot))}\"");
        builder.AppendLine("    \"FileMapping\"");
        builder.AppendLine("    {");
        builder.AppendLine("        \"LocalPath\" \"*\"");
        builder.AppendLine("        \"DepotPath\" \".\"");
        builder.AppendLine("        \"recursive\" \"1\"");
        builder.AppendLine("    }");

        if (!string.IsNullOrWhiteSpace(depot.InstallScript))
        {
            builder.AppendLine($"    \"InstallScript\" \"{Escape(_paths.Resolve(depot.InstallScript))}\"");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
