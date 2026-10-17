using System.IO;
using System.Reflection;
using System.Windows;

namespace WearableDistributor;

public partial class App : Application
{
    private static readonly object LogLock = new();
    private bool reportingFailure;

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            var path = WriteDiagnostic("Unhandled UI exception", args.Exception);
            if (!reportingFailure)
            {
                reportingFailure = true;
                MessageBox.Show("Skyrim SPID Patch Generator encountered an unexpected error and must close.\n\n" +
                    args.Exception.Message + (path is null ? "" : "\n\nDiagnostic details were saved to:\n" + path),
                    "Skyrim SPID Patch Generator · Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            // Preserve failure semantics. A corrupt application state must not continue generating patches.
            args.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteDiagnostic("Unhandled process exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => WriteDiagnostic("Unobserved background task exception", args.Exception);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        WriteDiagnostic("Application started · " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown version"));
        base.OnStartup(e);
    }

    internal static string? WriteDiagnostic(string message, Exception? exception = null)
    {
        try
        {
            var preferenceOverride = Environment.GetEnvironmentVariable("WEARABLE_DISTRIBUTOR_SETTINGS_DIR");
            var directory = Path.Combine(string.IsNullOrWhiteSpace(preferenceOverride)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WearableDistributor")
                : preferenceOverride, "Logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "application-" + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".log");
            lock (LogLock) File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " · " + message + Environment.NewLine + (exception is null ? "" : exception + Environment.NewLine));
            return path;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
