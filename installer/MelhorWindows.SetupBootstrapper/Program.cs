using System.Windows;
using Auralis.Setup;

namespace Auralis.Setup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var isSilent = args.Any(a => string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase));

        if (isSilent)
        {
            try
            {
                SetupWindow.RunInstallCore();
            }
            catch
            {
                // Silent mode: swallow errors, non-zero exit is not needed here
            }

            return;
        }

        var app = new Application();
        app.Run(new SetupWindow());
    }
}
