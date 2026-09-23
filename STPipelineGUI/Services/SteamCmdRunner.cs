using System.Diagnostics;
using System.IO;
using STPipelineGUI.Models;

namespace STPipelineGUI.Services;

public sealed class SteamCmdRunner
{
    private readonly PathService _paths;

    public SteamCmdRunner(PathService paths)
    {
        _paths = paths;
    }

    public async Task<int> RunAppBuildAsync(
        UserConfig config,
        string appBuildVdfPath,
        Action<string> onOutput,
        CancellationToken cancellationToken = default)
    {
        var steamCmdPath = _paths.Resolve(config.SteamCmdPath);
        if (!File.Exists(steamCmdPath))
        {
            onOutput($"SteamCMD no encontrado en: {steamCmdPath}");
            onOutput("Instala steamcmd.exe o ajusta config/user.json cuando quieras ejecutar builds reales.");
            return -1;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = steamCmdPath,
            WorkingDirectory = Path.GetDirectoryName(steamCmdPath) ?? _paths.RootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("+login");
        startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(config.Username) ? "anonymous" : config.Username);
        startInfo.ArgumentList.Add("+run_app_build");
        startInfo.ArgumentList.Add(appBuildVdfPath);
        startInfo.ArgumentList.Add("+quit");

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => AppendLine(args.Data, onOutput);
        process.ErrorDataReceived += (_, args) => AppendLine(args.Data, onOutput);

        onOutput($"Ejecutando SteamCMD: {steamCmdPath}");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        onOutput($"SteamCMD finalizo con codigo {process.ExitCode}.");

        return process.ExitCode;
    }

    private static void AppendLine(string? line, Action<string> onOutput)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            onOutput(line);
        }
    }
}
