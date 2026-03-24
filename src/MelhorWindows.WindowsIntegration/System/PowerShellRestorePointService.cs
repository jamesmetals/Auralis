using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
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

        var script =
            $$"""
            $ProgressPreference = 'SilentlyContinue'
            $InformationPreference = 'SilentlyContinue'
            $ErrorActionPreference = 'Stop'
            $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())

            if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
                Write-Output 'A criacao de ponto de restauracao exige executar o Auralis como administrador.'
                exit 1
            }

            try {
                Checkpoint-Computer -Description '{{EscapeSingleQuotedString(description)}}' -RestorePointType 'MODIFY_SETTINGS' | Out-Null
                Write-Output 'Ponto de restauracao criado.'
                exit 0
            }
            catch {
                $message = $_.Exception.Message

                if ([string]::IsNullOrWhiteSpace($message)) {
                    $message = $_.ToString()
                }

                Write-Output $message
                exit 1
            }
            """;
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

        var rawMessage = string.IsNullOrWhiteSpace(standardError)
            ? standardOutput
            : standardError;

        return OperationResult.Failure(
            string.IsNullOrWhiteSpace(rawMessage)
                ? $"PowerShell retornou codigo {process.ExitCode}."
                : NormalizeFailureMessage(rawMessage));
    }

    private static string EscapeSingleQuotedString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string NormalizeFailureMessage(string rawMessage)
    {
        var message = rawMessage
            .Replace("#< CLIXML", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_x000D__x000A_", Environment.NewLine, StringComparison.OrdinalIgnoreCase)
            .Replace("_x000D_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_x000A_", Environment.NewLine, StringComparison.OrdinalIgnoreCase);

        message = Regex.Replace(message, "<[^>]+>", " ");
        message = WebUtility.HtmlDecode(message);
        message = Regex.Replace(message, @"[ \t]{2,}", " ");
        message = Regex.Replace(message, @"(\r?\n){3,}", Environment.NewLine + Environment.NewLine);
        message = message.Trim();

        if (message.Contains(
                "A criacao de ponto de restauracao exige executar o Auralis como administrador",
                StringComparison.OrdinalIgnoreCase))
        {
            return "A criacao de ponto de restauracao exige executar o Auralis como administrador.";
        }

        if (message.Contains("Acesso negado", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
        {
            return "O Windows negou a criacao do ponto de restauracao. Execute o Auralis como administrador e confira se a Protecao do Sistema esta habilitada na unidade do Windows.";
        }

        if (message.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("desativad", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("turned off", StringComparison.OrdinalIgnoreCase))
        {
            return "A Protecao do Sistema parece desativada na unidade do Windows. Ative-a antes de pedir restore point automatico.";
        }

        if (message.Contains("already", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("restore point", StringComparison.OrdinalIgnoreCase))
        {
            return "O Windows bloqueou um novo ponto de restauracao neste momento. Tente novamente em alguns minutos ou desative a opcao automatica temporariamente.";
        }

        return string.IsNullOrWhiteSpace(message)
            ? "Nao foi possivel criar o ponto de restauracao."
            : message;
    }
}
