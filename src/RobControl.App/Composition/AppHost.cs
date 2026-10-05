// UseWPF drops System.IO from the implicit usings.
using System.IO;
using RobControl.App.Diagnostics;
using RobControl.App.ViewModels;
using RobControl.Core.Diagnostics;
using RobControl.Core.Events;
using RobControl.Core.Persistence;

namespace RobControl.App.Composition;

/// <summary>
/// Builds the object graph and owns everything with a lifetime. No container: the graph is a
/// handful of objects, and what talks to what should be readable off one page.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly ITraceLog _trace;
    private readonly FleetStore _store;
    private bool _disposed;

    public AppHost(IUiDispatcher dispatcher, ITraceLog? trace = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _trace = trace ?? NullTraceLog.Instance;

        _store = FleetStore.Open(Path.Combine(AppPaths.Data, "robcontrol.db"), _trace);
        _store.Info(EventCategory.App, null, BuildInfo.Describe());

        UserSettings settings = UserSettings.Load(_trace);
        ViewModel = new MainViewModel(dispatcher, _store, settings, BuildInfo.Stamp(), _trace);
    }

    public MainViewModel ViewModel { get; }

    /// <summary>
    /// Starts the schedule loop and the update check. Not in the constructor: the window should be
    /// up before anything that can take seconds.
    /// </summary>
    public async Task StartAsync()
    {
        _ = Task.Run(ViewModel.RunScheduleAsync);
        _ = Task.Run(ViewModel.RunChartRefreshAsync);
        _ = Task.Run(ViewModel.PruneTrends);

        AppSettings settings = AppSettings.Load(AppPaths.SettingsFile, _trace);
        UpdateResult result = await UpdateCheck.RunAsync(settings, BuildInfo.Version, trace: _trace).ConfigureAwait(true);
        ViewModel.UpdateStatus = result.IsUpdateAvailable ? result.StatusText : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trace.Info("Releasing the fleet database.");
        ViewModel.Dispose();
        _store.Dispose();
    }
}
