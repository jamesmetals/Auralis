using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;

namespace MelhorWindows.WindowsIntegration.System;

[SupportedOSPlatform("windows")]
public sealed class PowerShellRestorePointService : IWindowsRestorePointService
{
    public async Task<OperationResult> CreateRestorePointAsync(
        string description,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var script = $"Checkpoint-Computer -Description '{EscapeSingleQuotedString(description)}' -RestorePointType 'MODIFY_SETTINGS'";
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);

        if (process is null)
        {
            return OperationResult.Failure("Falha ao iniciar o PowerShell para criar o ponto de restauracao.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        if (process.ExitCode == 0)
        {
            return OperationResult.Success("Ponto de restauracao criado.");
        }

        var message = string.IsNullOrWhiteSpace(standardError)
            ? standardOutput
            : standardError;

        return OperationResult.Failure(
            string.IsNullOrWhiteSpace(message)
                ? $"PowerShell retornou codigo {process.ExitCode}."
                : message.Trim());
    }

    private static string EscapeSingleQuotedString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
