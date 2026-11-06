using System.Windows;
using System.Windows.Threading;
using RobControl.App.Appearance;
using RobControl.App.Composition;
using RobControl.App.Diagnostics;
using RobControl.App.ViewModels;
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

        // RobControl.exe --site "Plant 3" [--system "Line 4"] - opens that, without the picker.
        string? site = ArgumentAfter(e.Args, "--site");
        string? system = ArgumentAfter(e.Args, "--system");

        // The picker comes up before the main window. Until that window exists, closing the picker
        // must not end the application on its own - WPF's default is to quit when the last window closes.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        SystemChoice? choice;
        string? notice;
        try
        {
            _host = new AppHost(new WpfDispatcher(Dispatcher), _trace);
            choice = _host.DefaultChoice(site, system, out notice);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Report("RobControl could not start.", ex);
            Shutdown(1);
            return;
        }

        // The picker every time, unless the command line already says what to open: --site, or --robot
        // from NetControl's "Open in RobControl", which opens the system used last.
        bool ask = site is null && ArgumentAfter(e.Args, "--robot") is null;
        AppHost host = _host;
        while (true)
        {
            if (ask)
            {
                var picker = new SystemPickerWindow(new SystemPickerViewModel(host, choice, isStartup: true));
                if (picker.ShowDialog() != true || picker.Choice is null)
                {
                    Shutdown(0);
                    return;
                }

                choice = picker.Choice;
            }

            string? problem;
            try
            {
                problem = host.Open(choice);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Report("RobControl could not open that system.", ex);
                problem = ex.Message;
            }

            if (problem is null)
            {
                break;
            }

            MessageBox.Show(problem, "RobControl", MessageBoxButton.OK, MessageBoxImage.Warning);
            ask = true;
        }

        if (notice is not null)
        {
            host.ViewModel.ReportStatus(notice);
        }

        var shell = new MainWindow { DataContext = host.ViewModel };
        MainWindow = shell;
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        // A switch builds a new view model on the other system (and site); the window rebinds to it.
        host.ViewModelChanged += (_, _) => shell.DataContext = host.ViewModel;
        shell.Show();

        // RobControl.exe --robot 10.20.1.54 - how NetControl's "Open in RobControl" lands here. It
        // looks in the open site only: an address means nothing without the plant it is at.
        if (ArgumentAfter(e.Args, "--robot") is { } robot)
        {
            _host.ViewModel.SelectByAddress(robot);
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

    private static string? ArgumentAfter(string[] args, string name)
    {
        int at = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < args.Length && !string.IsNullOrWhiteSpace(args[at + 1]) ? args[at + 1] : null;
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
