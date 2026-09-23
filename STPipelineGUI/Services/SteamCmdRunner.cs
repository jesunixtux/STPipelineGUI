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

    public string ResolveSteamCmdPath(UserConfig config) => _paths.Resolve(config.SteamCmdPath);

    public bool CheckInstallation(UserConfig config, Action<string> onOutput)
    {
        var steamCmdPath = ResolveSteamCmdPath(config);
        if (File.Exists(steamCmdPath))
        {
            onOutput($"SteamCMD encontrado: {steamCmdPath}");
            return true;
        }

        onOutput($"SteamCMD no encontrado en: {steamCmdPath}");
        onOutput("Instala steamcmd.exe o ajusta config/user.json. Esta comprobacion no ejecuta uploads.");
        return false;
    }

    public Task<int> LoginAsync(
        UserConfig config,
        Action<string> onOutput,
        CancellationToken cancellationToken = default)
    {
        var username = string.IsNullOrWhiteSpace(config.Username) ? "anonymous" : config.Username;
        return RunSteamCmdAsync(config, onOutput, cancellationToken, "+login", username, "+quit");
    }

    public async Task<int> UploadBuildAsync(
        UserConfig config,
        string appBuildVdfPath,
        Action<string> onOutput,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(appBuildVdfPath))
        {
            onOutput($"No existe el app_build VDF: {appBuildVdfPath}");
            return -1;
        }

        var username = string.IsNullOrWhiteSpace(config.Username) ? "anonymous" : config.Username;
        return await RunSteamCmdAsync(config, onOutput, cancellationToken, "+login", username, "+run_app_build", appBuildVdfPath, "+quit");
    }

    private async Task<int> RunSteamCmdAsync(
        UserConfig config,
        Action<string> onOutput,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var steamCmdPath = ResolveSteamCmdPath(config);
        if (!File.Exists(steamCmdPath))
        {
            onOutput($"SteamCMD no encontrado en: {steamCmdPath}");
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

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

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
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        onOutput(line);

        if (line.Contains("Steam Guard", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("two-factor", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("2FA", StringComparison.OrdinalIgnoreCase))
        {
            onOutput("Steam Guard requiere una nueva autenticacion. Completa el login fuera de la app o agrega el flujo 2FA en una fase posterior.");
        }
    }
}
