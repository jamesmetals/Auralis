using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace Auralis.Setup;

public partial class SetupWindow : Window
{
    private static readonly string InstallRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Auralis");

    private readonly string? _installedVersion;
    private readonly string _payloadVersion;

    public SetupWindow()
    {
        _installedVersion = ReadInstalledVersion();
        _payloadVersion = ReadPayloadVersion(AppContext.BaseDirectory);
        InitializeComponent();
        ApplyWelcomeState();
    }

    // ── Welcome state ────────────────────────────────────────────────────────

    private void ApplyWelcomeState()
    {
        var locationText = $"Local: {InstallRoot}";

        if (_installedVersion is null)
        {
            WelcomeTitleText.Text = "Instalar o Auralis";
            WelcomeVersionText.Text = $"Versão {_payloadVersion}";
            WelcomeLocationText.Text = locationText;
            InstallButton.Content = "Instalar";
        }
        else if (_installedVersion == _payloadVersion)
        {
            WelcomeTitleText.Text = "Auralis já está instalado";
            WelcomeVersionText.Text = $"Versão {_installedVersion} está instalada. Deseja reinstalar?";
            WelcomeLocationText.Text = locationText;
            InstallButton.Content = "Reinstalar";
        }
        else
        {
            WelcomeTitleText.Text = "Atualização disponível";
            WelcomeVersionText.Text = $"Instalada: {_installedVersion}  →  Nova: {_payloadVersion}";
            WelcomeLocationText.Text = locationText;
            InstallButton.Content = "Atualizar";
            InstallingTitleText.Text = "Atualizando Auralis...";
        }
    }

    // ── Button handlers ──────────────────────────────────────────────────────

    private async void OnInstallClicked(object sender, RoutedEventArgs e)
    {
        ShowPanel(InstallingPanel);
        InstallButton.IsEnabled = false;

        var progress = new Progress<(double Value, string Label)>(report =>
        {
            InstallProgressBar.Value = report.Value * 100;
            InstallStatusText.Text = report.Label;
            InstallPercentText.Text = $"{(int)(report.Value * 100)}%";
        });

        try
        {
            await Task.Run(() => RunInstallCore(progress));
            ShowPanel(CompletePanel);
            CompleteVersionText.Text = $"Versão {_payloadVersion} instalada em:\n{InstallRoot}";
        }
        catch (Exception ex)
        {
            ShowPanel(ErrorPanel);
            ErrorMessageText.Text = ex.Message;
        }
    }

    private void OnLaunchClicked(object sender, RoutedEventArgs e)
    {
        var exePath = Path.Combine(InstallRoot, "Auralis.exe");

        if (File.Exists(exePath))
        {
            Process.Start(new ProcessStartInfo(exePath, "--open-dashboard") { UseShellExecute = true });
        }

        Close();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    // ── Panel switching ──────────────────────────────────────────────────────

    private void ShowPanel(System.Windows.UIElement panel)
    {
        WelcomePanel.Visibility = Visibility.Collapsed;
        InstallingPanel.Visibility = Visibility.Collapsed;
        CompletePanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;
    }

    // ── Core installer logic (also called from Program for --silent mode) ────

    internal static void RunInstallCore(IProgress<(double, string)>? progress = null)
    {
        void Report(double p, string s) => progress?.Report((p, s));

        var payloadZip = Path.Combine(AppContext.BaseDirectory, "Auralis.Payload.zip");

        if (!File.Exists(payloadZip))
            throw new FileNotFoundException("Pacote de instalação não encontrado.", payloadZip);

        var tempDir = Path.Combine(
            Path.GetTempPath(), "Auralis.Setup." + Guid.NewGuid().ToString("N"));

        try
        {
            Report(0.05, "Extraindo arquivos...");
            Directory.CreateDirectory(tempDir);
            ZipFile.ExtractToDirectory(payloadZip, tempDir, overwriteFiles: true);

            Report(0.22, "Encerrando processos em execução...");
            StopRunningProcesses();

            Report(0.35, "Removendo versão anterior...");
            if (Directory.Exists(InstallRoot))
                Directory.Delete(InstallRoot, recursive: true);

            Report(0.50, "Copiando arquivos...");
            Directory.CreateDirectory(InstallRoot);
            CopyDirectory(tempDir, InstallRoot);

            Report(0.68, "Configurando integração com o Explorer...");
            var exePath = Path.Combine(InstallRoot, "Auralis.exe");
            RegisterFolderVerb(exePath);

            Report(0.78, "Registrando no sistema...");
            RegisterUninstallEntry(exePath);

            Report(0.88, "Criando atalhos...");
            CreateShortcuts(exePath);

            Report(1.00, "Concluído!");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    // ── Install steps ────────────────────────────────────────────────────────

    private static void StopRunningProcesses()
    {
        foreach (var process in Process.GetProcessesByName("Auralis"))
        {
            try
            {
                if (process.MainModule?.FileName?.StartsWith(InstallRoot,
                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // best-effort
            }
        }

        Thread.Sleep(600);
    }

    private static void RegisterFolderVerb(string exePath)
    {
        // Remove legacy entries from previous versions
        Registry.CurrentUser.DeleteSubKeyTree(
            @"Software\Classes\Folder\shell\Auralis", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(
            @"Software\Classes\Directory\shell\Auralis.ChangeFolderIcon", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(
            @"Software\Classes\Folder\shell\Auralis.ChangeFolderIcon", throwOnMissingSubKey: false);

        const string verbPath = @"Software\Classes\Directory\shell\Auralis";

        using var verbKey = Registry.CurrentUser.CreateSubKey(verbPath, writable: true);
        verbKey.SetValue("MUIVerb", "Auralis");
        verbKey.SetValue("Icon", exePath);
        verbKey.SetValue("Position", "Top");
        verbKey.SetValue("MultiSelectModel", "Single");
        verbKey.SetValue("NeverDefault", "");
        verbKey.SetValue("SeparatorBefore", "");
        verbKey.SetValue("SeparatorAfter", "");
        verbKey.DeleteValue("SubCommands", throwOnMissingValue: false);
        verbKey.DeleteSubKeyTree("shell", throwOnMissingSubKey: false);

        using var commandKey = verbKey.CreateSubKey("command", writable: true);
        commandKey.SetValue("", $"\"{exePath}\" \"%1\"");
    }

    private static void RegisterUninstallEntry(string exePath)
    {
        var version = FileVersionInfo.GetVersionInfo(exePath).FileVersion ?? "1.0.0";
        var uninstallScript = Path.Combine(InstallRoot, "Uninstall-Auralis.ps1");
        var uninstallCmd =
            $"powershell.exe -ExecutionPolicy Bypass -NoProfile -File \"{uninstallScript}\"";

        const string keyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Auralis";

        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        key.SetValue("DisplayName", "Auralis");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "Auralis");
        key.SetValue("InstallLocation", InstallRoot);
        key.SetValue("DisplayIcon", exePath);
        key.SetValue("UninstallString", uninstallCmd);
        key.SetValue("QuietUninstallString", $"{uninstallCmd} -Silent");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void CreateShortcuts(string exePath)
    {
        var desktop = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Auralis.lnk");
        var startMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Auralis", "Auralis.lnk");
        const string launchArguments = "--open-dashboard";

        CreateShortcut(desktop, exePath, InstallRoot, launchArguments);
        CreateShortcut(startMenu, exePath, InstallRoot, launchArguments);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string? arguments = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDir;
        shortcut.Arguments = arguments ?? string.Empty;
        shortcut.IconLocation = targetPath;
        shortcut.Save();
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    // ── Version helpers ──────────────────────────────────────────────────────

    private static string? ReadInstalledVersion()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Auralis");
        return key?.GetValue("DisplayVersion") as string;
    }

    private static string ReadPayloadVersion(string baseDir)
    {
        var versionFile = Path.Combine(baseDir, "payload-version.txt");

        if (File.Exists(versionFile))
            return File.ReadAllText(versionFile).Trim();

        return "1.0.0";
    }
}
