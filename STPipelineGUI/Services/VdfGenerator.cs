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
        var outputRoot = _paths.Resolve(project.BuildOutput);
        Directory.CreateDirectory(outputRoot);

        var depotFiles = new List<string>();
        foreach (var depot in project.Depots.Where(d => IsPlatformSelected(project, d.Platform)))
        {
            var depotPath = Path.Combine(outputRoot, $"depot_{depot.DepotId}.vdf");
            File.WriteAllText(depotPath, BuildDepotVdf(depot));
            depotFiles.Add(depotPath);
        }

        var appBuildPath = Path.Combine(outputRoot, $"app_build_{project.AppId}.vdf");
        File.WriteAllText(appBuildPath, BuildAppVdf(project, depotFiles));

        return depotFiles.Prepend(appBuildPath).ToList();
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
        var builder = new StringBuilder();
        builder.AppendLine("\"appbuild\"");
        builder.AppendLine("{");
        builder.AppendLine($"    \"appid\" \"{Escape(project.AppId)}\"");
        builder.AppendLine($"    \"desc\" \"{Escape(project.Description)}\"");
        builder.AppendLine($"    \"buildoutput\" \"{Escape(_paths.Resolve(project.BuildOutput))}\"");
        builder.AppendLine($"    \"contentroot\" \"{Escape(_paths.Resolve(project.ContentRoot))}\"");
        builder.AppendLine($"    \"setlive\" \"{Escape(project.Branch)}\"");
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
