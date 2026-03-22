using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace MelhorWindows.Desktop;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        SplashWindow? splashWindow = null;

        try
        {
            splashWindow = new SplashWindow();
            splashWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.Background);

            var startupStopwatch = Stopwatch.StartNew();
            var minimumSplashTime = TimeSpan.FromMilliseconds(900);

            var mainWindow = new MainWindow();
            await mainWindow.InitializeAsync(splashWindow.UpdateState);

            var remainingSplashTime = minimumSplashTime - startupStopwatch.Elapsed;

            if (remainingSplashTime > TimeSpan.Zero)
            {
                await Task.Delay(remainingSplashTime);
            }

            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
            splashWindow.Close();
        }
        catch (Exception exception)
        {
            splashWindow?.Close();
            System.Windows.MessageBox.Show(
                $"Nao foi possivel iniciar o Auralis.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Auralis",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
