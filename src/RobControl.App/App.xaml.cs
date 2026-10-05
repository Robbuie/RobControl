using System.Windows;
using System.Windows.Threading;
using RobControl.App.Appearance;
using RobControl.App.Composition;
using RobControl.App.Diagnostics;
using RobControl.App.Views;
using RobControl.Core.Diagnostics;

namespace RobControl.App;

/// <summary>
/// Application entry point. Builds the object graph, shows the window, and releases the fleet
/// database on the way out. Same shape as NetControl's, for the same reason: this is a WinExe, so
/// nothing is allowed to fail without a window or a dialog that names the cause.
/// </summary>
public partial class App : Application
{
    private AppHost? _host;
    private TraceLog? _trace;
    private IDisposable? _systemTheme;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        _trace = TraceLog.Open(AppPaths.Logs);
        _trace.Info(BuildInfo.Describe());

        Theme.Apply(this, AppearanceStore.Load(_trace));
        WindowChromeCommands.Register();

        _systemTheme = SystemTheme.Watch(() =>
        {
            if (Dispatcher.HasShutdownStarted)
            {
                return;
            }

            Dispatcher.InvokeAsync(() =>
            {
                if (Theme.CurrentChoice.Follow == Theme.FollowWindows)
                {
                    Theme.Apply(this, Theme.CurrentChoice);
                }
            });
        });

        try
        {
            _host = new AppHost(new WpfDispatcher(Dispatcher), _trace);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Report("RobControl could not start.", ex);
            Shutdown(1);
            return;
        }

        var shell = new MainWindow { DataContext = _host.ViewModel };
        shell.Show();

        // RobControl.exe --robot 10.20.1.54 - how NetControl's "Open in RobControl" lands here.
        string[] args = e.Args;
        int at = Array.FindIndex(args, a => string.Equals(a, "--robot", StringComparison.OrdinalIgnoreCase));
        if (at >= 0 && at + 1 < args.Length)
        {
            _host.ViewModel.SelectByAddress(args[at + 1]);
        }

        UpdateApplier.SweepLeftovers(BuildInfo.ExecutablePath, AppPaths.Updates, _trace);
        _ = _host.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _host = null;

        _systemTheme?.Dispose();
        _systemTheme = null;

        _trace?.Write(Core.Persistence.EventSeverity.Info, "Stopped.");
        _trace?.Dispose();
        _trace = null;

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report("RobControl hit an error it did not expect.", e.Exception);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Report("RobControl stopped because of an error on a background thread.", exception);
        }
    }

    /// <summary>Says what went wrong twice: to the diagnostic log, and to whoever is standing there.</summary>
    private void Report(string headline, Exception exception)
    {
        _trace?.Write(Core.Persistence.EventSeverity.Error, headline, exception);

        string remediation = exception is Core.RobControlException { Remediation: { } next }
            ? Environment.NewLine + Environment.NewLine + next
            : string.Empty;

        string written = _trace?.FilePath is { } path
            ? Environment.NewLine + Environment.NewLine + $"Written to {path}"
            : string.Empty;

        MessageBox.Show(
            $"{headline}{Environment.NewLine}{Environment.NewLine}{exception.GetType().Name}: "
                + $"{exception.Message}{remediation}{written}"
                + $"{Environment.NewLine}{Environment.NewLine}{exception.StackTrace}",
            "RobControl",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
