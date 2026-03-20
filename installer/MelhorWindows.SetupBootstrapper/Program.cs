using System.Diagnostics;
using System.Windows.Forms;

var isSilent = args.Any(argument => string.Equals(argument, "--silent", StringComparison.OrdinalIgnoreCase));
var baseDirectory = AppContext.BaseDirectory;
var installScriptPath = Path.Combine(baseDirectory, "Install-MelhorWindows.ps1");
var payloadPath = Path.Combine(baseDirectory, "MelhorWindows.Payload.zip");

if (!File.Exists(installScriptPath) || !File.Exists(payloadPath))
{
    ShowError(
        "Arquivos do instalador nao encontrados. Execute o setup a partir da pasta gerada em installer\\dist.",
        isSilent);
    return 1;
}

var installArguments = $"-ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -File \"{installScriptPath}\"";
var processStartInfo = new ProcessStartInfo
{
    FileName = "powershell.exe",
    Arguments = installArguments,
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true
};

try
{
    using var process = Process.Start(processStartInfo);
    if (process is null)
    {
        ShowError("Nao foi possivel iniciar o instalador do MelhorWindows.", isSilent);
        return 1;
    }

    var standardOutput = process.StandardOutput.ReadToEnd();
    var standardError = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        var errorMessage = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
        ShowError($"A instalacao falhou.{Environment.NewLine}{Environment.NewLine}{errorMessage}".Trim(), isSilent);
        return process.ExitCode;
    }

    if (!isSilent)
    {
        MessageBox.Show(
            "MelhorWindows instalado com sucesso.",
            "MelhorWindows Setup",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    return 0;
}
catch (Exception exception)
{
    ShowError($"Falha ao executar o instalador.{Environment.NewLine}{Environment.NewLine}{exception.Message}", isSilent);
    return 1;
}

static void ShowError(string message, bool isSilent)
{
    if (isSilent)
    {
        return;
    }

    MessageBox.Show(
        message,
        "MelhorWindows Setup",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
}
