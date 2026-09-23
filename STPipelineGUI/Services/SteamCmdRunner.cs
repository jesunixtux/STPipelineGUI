using System.Diagnostics;
using System.IO;
using STPipelineGUI.Models;

namespace STPipelineGUI.Services;

public sealed record SteamCmdCredentials(string Password, string GuardCode);

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

    public async Task<int> CheckInstallationAsync(
        UserConfig config,
        Action<string> onOutput,
        CancellationToken cancellationToken = default)
    {
        var steamCmdPath = ResolveSteamCmdPath(config);
        if (!File.Exists(steamCmdPath))
        {
            onOutput($"SteamCMD no encontrado en: {steamCmdPath}");
            return -1;
        }

        onOutput("Comprobando SteamCMD con +quit. No se ejecutará ninguna subida.");
        return await RunSteamCmdAsync(config, onOutput, cancellationToken, null, "+quit");
    }

    public Task<int> LoginAsync(
        UserConfig config,
        Action<string> onOutput,
        CancellationToken cancellationToken = default,
        SteamCmdCredentials? credentials = null)
    {
        var username = string.IsNullOrWhiteSpace(config.Username) ? "anonymous" : config.Username;
        return RunSteamCmdAsync(config, onOutput, cancellationToken, credentials, "+login", username, "+quit");
    }

    public async Task<int> UploadBuildAsync(
        UserConfig config,
        string appBuildVdfPath,
        Action<string> onOutput,
        CancellationToken cancellationToken = default,
        SteamCmdCredentials? credentials = null)
    {
        if (!File.Exists(appBuildVdfPath))
        {
            onOutput($"No existe el app_build VDF: {appBuildVdfPath}");
            return -1;
        }

        var username = string.IsNullOrWhiteSpace(config.Username) ? "anonymous" : config.Username;
        return await RunSteamCmdAsync(config, onOutput, cancellationToken, credentials, "+login", username, "+run_app_build", appBuildVdfPath, "+quit");
    }

    private async Task<int> RunSteamCmdAsync(
        UserConfig config,
        Action<string> onOutput,
        CancellationToken cancellationToken,
        SteamCmdCredentials? credentials,
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
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var inputLock = new object();
        var passwordSent = false;
        var guardCodeSent = false;

        void HandleLine(string? line)
        {
            AppendLine(line, onOutput);
            if (string.IsNullOrWhiteSpace(line) || credentials is null)
            {
                return;
            }

            if (!passwordSent && !string.IsNullOrEmpty(credentials.Password) && IsPasswordPrompt(line))
            {
                SendInput(credentials.Password, ref passwordSent, inputLock, process, onOutput);
            }

            if (!guardCodeSent && !string.IsNullOrEmpty(credentials.GuardCode) && IsGuardCodePrompt(line))
            {
                SendInput(credentials.GuardCode, ref guardCodeSent, inputLock, process, onOutput);
            }
        }

        process.OutputDataReceived += (_, args) => HandleLine(args.Data);
        process.ErrorDataReceived += (_, args) => HandleLine(args.Data);

        onOutput($"Ejecutando SteamCMD: {steamCmdPath}");
        onOutput($"SteamCMD args: {string.Join(' ', arguments.Select(SanitizeArgument))}");
        if (!process.Start())
        {
            onOutput("SteamCMD no pudo iniciar el proceso.");
            return -1;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            onOutput("SteamCMD cancelado.");
            return -2;
        }

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

    private static void SendInput(
        string secret,
        ref bool sent,
        object inputLock,
        Process process,
        Action<string> onOutput)
    {
        lock (inputLock)
        {
            if (sent || process.HasExited)
            {
                return;
            }

            process.StandardInput.WriteLine(secret);
            process.StandardInput.Flush();
            sent = true;
            onOutput("Credencial enviada a SteamCMD sin mostrarla en la consola.");
        }
    }

    private static bool IsPasswordPrompt(string line)
    {
        return line.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("contraseña", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGuardCodePrompt(string line)
    {
        return line.Contains("steam guard", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("two-factor", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("2fa", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("auth code", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeArgument(string argument)
    {
        return argument.Contains("password", StringComparison.OrdinalIgnoreCase)
            ? "[hidden]"
            : argument;
    }
}
