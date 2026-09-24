using System.Diagnostics;
using System.IO;
using System.Text;
using STPipelineGUI.Models;

namespace STPipelineGUI.Services;

public sealed record SteamCmdCredentials(string Password, string GuardCode);

public sealed class SteamCmdRunner
{
    private readonly PathService _paths;
    private readonly object _processLock = new();
    private readonly object _inputLock = new();
    private Process? _activeProcess;

    public SteamCmdRunner(PathService paths)
    {
        _paths = paths;
    }

    public string ResolveSteamCmdPath(UserConfig config) => _paths.Resolve(config.SteamCmdPath);

    public bool TrySendInput(string input, Action<string> onOutput)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        Process? process;
        lock (_processLock)
        {
            process = _activeProcess;
        }

        if (process is null)
        {
            EmitLine(onOutput, "No hay una sesión SteamCMD activa esperando entrada.");
            return false;
        }

        try
        {
            lock (_inputLock)
            {
                if (process.HasExited)
                {
                    EmitLine(onOutput, "La sesión SteamCMD ya terminó.");
                    return false;
                }

                process.StandardInput.WriteLine(input);
                process.StandardInput.Flush();
            }

            EmitLine(onOutput, "Entrada enviada a SteamCMD sin mostrarla en la consola.");
            return true;
        }
        catch (InvalidOperationException)
        {
            EmitLine(onOutput, "SteamCMD ya no acepta entrada en este momento.");
            return false;
        }
        catch (IOException)
        {
            EmitLine(onOutput, "No se pudo enviar la entrada a SteamCMD.");
            return false;
        }
    }

    public bool CheckInstallation(UserConfig config, Action<string> onOutput)
    {
        var steamCmdPath = ResolveSteamCmdPath(config);
        if (File.Exists(steamCmdPath))
        {
            EmitLine(onOutput, $"SteamCMD encontrado: {steamCmdPath}");
            return true;
        }

        EmitLine(onOutput, $"SteamCMD no encontrado en: {steamCmdPath}");
        EmitLine(onOutput, "Instala steamcmd.exe o ajusta config/user.json. Esta comprobacion no ejecuta uploads.");
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
            EmitLine(onOutput, $"SteamCMD no encontrado en: {steamCmdPath}");
            return -1;
        }

        EmitLine(onOutput, "Comprobando SteamCMD con +quit. No se ejecutará ninguna subida.");
        return await RunSteamCmdAsync(config, onOutput, cancellationToken, null, "+quit");
    }

    public Task<int> LoginAsync(
        UserConfig config,
        Action<string> onOutput,
        CancellationToken cancellationToken = default,
        SteamCmdCredentials? credentials = null)
    {
        if (string.IsNullOrWhiteSpace(config.Username))
        {
            EmitLine(onOutput, "Configura tu usuario de Steam antes de iniciar sesión.");
            return Task.FromResult(-1);
        }

        var username = config.Username.Trim();
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
            EmitLine(onOutput, $"No existe el app_build VDF: {appBuildVdfPath}");
            return -1;
        }

        if (string.IsNullOrWhiteSpace(config.Username))
        {
            EmitLine(onOutput, "Configura tu usuario de Steam antes de subir una build.");
            return -1;
        }

        var username = config.Username.Trim();
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
            EmitLine(onOutput, $"SteamCMD no encontrado en: {steamCmdPath}");
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
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var passwordSent = false;
        var guardCodeSent = false;
        var guardPromptReported = false;
        var promptBuffer = new StringBuilder();
        var promptLock = new object();

        void HandleChunk(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            onOutput(chunk);

            string promptText;
            lock (promptLock)
            {
                promptBuffer.Append(chunk);
                if (promptBuffer.Length > 2048)
                {
                    promptBuffer.Remove(0, promptBuffer.Length - 2048);
                }

                promptText = promptBuffer.ToString();
            }

            if (!guardPromptReported && IsGuardCodePrompt(promptText))
            {
                EmitLine(onOutput, "Steam Guard requiere una nueva autenticacion. Usa el campo de entrada de la consola para enviar el codigo.");
                guardPromptReported = true;
            }

            if (credentials is null)
            {
                return;
            }

            if (!passwordSent && !string.IsNullOrEmpty(credentials.Password) && IsPasswordPrompt(promptText))
            {
                SendInput(credentials.Password, ref passwordSent, process, onOutput);
            }

            if (!guardCodeSent && !string.IsNullOrEmpty(credentials.GuardCode) && IsGuardCodePrompt(promptText))
            {
                SendInput(credentials.GuardCode, ref guardCodeSent, process, onOutput);
            }
        }

        EmitLine(onOutput, $"Ejecutando SteamCMD: {steamCmdPath}");
        EmitLine(onOutput, $"SteamCMD args: {string.Join(' ', arguments.Select(SanitizeArgument))}");
        if (!process.Start())
        {
            EmitLine(onOutput, "SteamCMD no pudo iniciar el proceso.");
            return -1;
        }

        lock (_processLock)
        {
            _activeProcess = process;
        }

        var standardOutputTask = ReadStreamAsync(process.StandardOutput, HandleChunk, cancellationToken);
        var standardErrorTask = ReadStreamAsync(process.StandardError, HandleChunk, cancellationToken);

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

            try
            {
                await Task.WhenAll(standardOutputTask, standardErrorTask);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The readers are expected to stop with the same cancellation token.
            }

            EmitLine(onOutput, "SteamCMD cancelado.");
            ClearActiveProcess(process);
            return -2;
        }

        try
        {
            await Task.WhenAll(standardOutputTask, standardErrorTask);
        }
        finally
        {
            ClearActiveProcess(process);
        }

        EmitLine(onOutput, $"SteamCMD finalizo con codigo {process.ExitCode}.");

        return process.ExitCode;
    }

    private static async Task ReadStreamAsync(
        StreamReader reader,
        Action<string> onOutput,
        CancellationToken cancellationToken)
    {
        var buffer = new char[1024];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0)
            {
                return;
            }

            onOutput(new string(buffer, 0, count));
        }
    }

    private static void EmitLine(Action<string> onOutput, string message)
    {
        onOutput(message + Environment.NewLine);
    }

    private void SendInput(
        string secret,
        ref bool sent,
        Process process,
        Action<string> onOutput)
    {
        lock (_inputLock)
        {
            if (sent || process.HasExited)
            {
                return;
            }

            process.StandardInput.WriteLine(secret);
            process.StandardInput.Flush();
            sent = true;
            EmitLine(onOutput, "Credencial enviada a SteamCMD sin mostrarla en la consola.");
        }
    }

    private void ClearActiveProcess(Process process)
    {
        lock (_processLock)
        {
            if (ReferenceEquals(_activeProcess, process))
            {
                _activeProcess = null;
            }
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
