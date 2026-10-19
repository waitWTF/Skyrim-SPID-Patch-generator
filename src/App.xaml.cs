using System.Windows;
using System.Diagnostics;

namespace WearableDistributor;

public partial class App : Application
{
    private bool reportingFailure;

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            var path = WriteDiagnostic(DiagnosticEvent.UnhandledUiError, args.Exception, DiagnosticLevel.Fatal);
            if (!reportingFailure)
            {
                reportingFailure = true;
                MessageBox.Show("Skyrim SPID Patch Generator encountered an unexpected error and must close.\n\n" +
                    "Error code: APP-1002" + (path is null ? "\n\nA log could not be saved to disk." : "\n\nA shareable log was saved automatically:\n" + path),
                    "Skyrim SPID Patch Generator · Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            // Preserve failure semantics. A corrupt application state must not continue generating patches.
            args.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteDiagnostic(DiagnosticEvent.UnhandledProcessError, args.ExceptionObject as Exception, DiagnosticLevel.Fatal);
        TaskScheduler.UnobservedTaskException += (_, args) => WriteDiagnostic(DiagnosticEvent.UnobservedTaskError, args.Exception);
        PresentationTraceSources.DataBindingSource.Listeners.Clear();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new SafeBindingListener());
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        WriteDiagnostic(DiagnosticEvent.ApplicationStarted, level: DiagnosticLevel.Information);
        base.OnStartup(e);
        var splash = new StartupWindow();
        splash.Show();
        // Let WPF render the icon before loading preferences and creating the main workspace.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var workspace = new MainWindow();
            MainWindow = workspace;
            workspace.ContentRendered += OnReady;
            workspace.Show();
            void OnReady(object? sender, EventArgs args)
            {
                workspace.ContentRendered -= OnReady;
                splash.Close();
            }
        }));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WriteDiagnostic(DiagnosticEvent.ApplicationStopped, level: DiagnosticLevel.Information);
        base.OnExit(e);
    }

    internal static string? WriteDiagnostic(DiagnosticEvent eventId, Exception? exception = null, DiagnosticLevel level = DiagnosticLevel.Error) => SupportLog.Record(eventId, exception, level);

    private sealed class SafeBindingListener : TraceListener
    {
        public override void Write(string? message) => SupportLog.Record(DiagnosticEvent.UiBindingFailed);
        public override void WriteLine(string? message) => SupportLog.Record(DiagnosticEvent.UiBindingFailed);
        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message) => SupportLog.Record(DiagnosticEvent.UiBindingFailed);
        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args) => SupportLog.Record(DiagnosticEvent.UiBindingFailed);
    }
}
