namespace MelhorWindows.FolderMonitorWorker;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0) return;

        var targetFolder = args[0];
        if (!Directory.Exists(targetFolder)) return;

        using var monitor = new MonitorService(targetFolder);
        monitor.Start();
    }
}
