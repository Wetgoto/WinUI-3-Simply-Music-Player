using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Music.Services;

namespace Music;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => LogStartupError(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogStartupError(exception);
            }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _ = SetCurrentProcessExplicitAppUserModelID("Music.Desktop.Player");
            var window = new MainWindow();
            _window = window;
            window.Activate();
            _ = window.InitializeAsync(Environment.GetCommandLineArgs().Skip(1).ToArray());
        }
        catch (Exception exception)
        {
            LogStartupError(exception);
            throw;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private static void LogStartupError(Exception exception)
    {
        try
        {
            var logPath = Path.Combine(AppPaths.LogsDirectory, "startup-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(
                logPath,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never hide the original failure.
        }
    }
}

