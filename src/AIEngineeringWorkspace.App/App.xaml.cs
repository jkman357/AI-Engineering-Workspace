using System.Windows;
using AIEngineeringWorkspace.Infrastructure;

namespace AIEngineeringWorkspace;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        RuntimeLog.Initialize();
        RuntimeLog.Info($"Application starting. Version={AppInfo.DisplayVersion}; PID={Environment.ProcessId}; OS={Environment.OSVersion}; CLR={Environment.Version}");

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += (_, args) =>
        {
            RuntimeLog.Fatal("Dispatcher unhandled exception.", args.Exception);
            args.Handled = false;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            RuntimeLog.Error("Unobserved task exception.", args.Exception);
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        RuntimeLog.Info($"Application exiting. ExitCode={e.ApplicationExitCode}");
        RuntimeLog.Shutdown();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            RuntimeLog.Fatal($"AppDomain unhandled exception. IsTerminating={e.IsTerminating}", ex);
        }
        else
        {
            RuntimeLog.Fatal($"AppDomain unhandled non-Exception object. IsTerminating={e.IsTerminating}");
        }
    }
}
