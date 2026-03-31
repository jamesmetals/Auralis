using System.Diagnostics;
using System.Reflection;

namespace MelhorWindows.FolderMonitorWorker;

public sealed class MonitorService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly string _targetFolder;
    private readonly string _appFolder;
    private readonly string _logFilePath;

    public MonitorService(string targetFolder)
    {
        _targetFolder = targetFolder;
        _appFolder = Path.Combine(_targetFolder, "FolderMonitor_Logs");
        _logFilePath = Path.Combine(_appFolder, "logs.txt");
    }

    public void Start()
    {
        try
        {
            if (!Directory.Exists(_appFolder))
            {
                var di = Directory.CreateDirectory(_appFolder);

                try
                {
                    var exePath = Process.GetCurrentProcess().MainModule?.FileName
                                  ?? Assembly.GetExecutingAssembly().Location;
                    var desktopIniPath = Path.Combine(_appFolder, "desktop.ini");

                    string[] iniLines =
                    {
                        "[.ShellClassInfo]",
                        $"IconResource={exePath},0"
                    };

                    File.WriteAllLines(desktopIniPath, iniLines);
                    File.SetAttributes(desktopIniPath, FileAttributes.Hidden | FileAttributes.System);
                    di.Attributes |= FileAttributes.ReadOnly;
                }
                catch { }
            }

            if (!File.Exists(_logFilePath))
                File.WriteAllText(_logFilePath, $"--- Inicio do Monitoramento: {DateTime.Now} ---\n");
            else
                WriteLog($"--- Retomada do Monitoramento: {DateTime.Now} ---");

            _watcher = new FileSystemWatcher(_targetFolder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                Filter = "*.*"
            };

            _watcher.Created += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.EnableRaisingEvents = true;

            Thread.Sleep(Timeout.Infinite);
        }
        catch (Exception ex)
        {
            WriteLog($"Erro critico na inicializacao: {ex.Message}");
        }
    }

    private bool IsLogFile(string path) =>
        path.StartsWith(_appFolder, StringComparison.OrdinalIgnoreCase);

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (IsLogFile(e.FullPath)) return;
        WriteLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.ChangeType}: {e.FullPath}");
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (IsLogFile(e.FullPath) && IsLogFile(e.OldFullPath)) return;
        WriteLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Renamed: '{e.OldFullPath}' -> '{e.FullPath}'");
    }

    private void WriteLog(string message)
    {
        try
        {
            using var stream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(message);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
    }
}
