using System.Collections.ObjectModel;
using System.Globalization;
// UseWPF drops System.IO from the implicit usings.
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobControl.Core.Backup;
using RobControl.Core.History;

namespace RobControl.App.ViewModels;

/// <summary>
/// Backups opened from a folder, with no site: a stick, a share, someone else's laptop. Browse, compare,
/// search, alarm history and the cell timeline, and What changed - all from the files, read-only. No robot
/// is contacted and nothing is written to the folder; there is no database behind it.
/// </summary>
public sealed class LooseViewModel : ObservableObject
{
    private readonly LooseArchive _loose;
    private readonly string _tool;
    private RobotRowViewModel? _selected;
    private CompareViewModel? _compare;
    private string _status;

    public LooseViewModel(LooseArchive loose, string tool)
    {
        _loose = loose ?? throw new ArgumentNullException(nameof(loose));
        _tool = tool ?? string.Empty;
        foreach (Core.Robots.Robot robot in loose.Robots)
        {
            var row = new RobotRowViewModel(robot);
            row.ApplyBackups(loose.Archive.List(robot));
            Robots.Add(row);
        }

        Search = new SearchViewModel(Snapshot);
        Alarms = new AlarmsViewModel(Snapshot);
        Timeline = new AlarmTimelineViewModel(Snapshot, () => new Dictionary<string, TimeSpan>());
        CompareCommand = new RelayCommand(CompareChecked, () => Selected is { Backups.Count: >= 2 });
        WhatChangedCommand = new AsyncRelayCommand(WhatChangedAsync);
        OpenFolderCommand = new RelayCommand(() => OpenFolder?.Invoke(Selected is { } row ? _loose.Archive.RobotFolder(row.Robot) : _loose.Folder));
        OpenBackupFolderCommand = new RelayCommand<BackupRowViewModel>(row => OpenFolder?.Invoke(row?.Folder ?? _loose.Folder));
        Selected = Robots.FirstOrDefault();
        _status = "Read-only: nothing here is written to the folder or sent to a robot. " + Describe();
    }

    public string Title => $"Backups from a folder - {_loose.Folder}";

    public string Folder => _loose.Folder;

    public ObservableCollection<RobotRowViewModel> Robots { get; } = [];

    public RobotRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                CompareCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public CompareViewModel? Compare
    {
        get => _compare;
        private set => SetProperty(ref _compare, value);
    }

    public SearchViewModel Search { get; }

    public AlarmsViewModel Alarms { get; }

    public AlarmTimelineViewModel Timeline { get; }

    public IRelayCommand CompareCommand { get; }

    public IAsyncRelayCommand WhatChangedCommand { get; }

    public IRelayCommand OpenFolderCommand { get; }

    public IRelayCommand<BackupRowViewModel> OpenBackupFolderCommand { get; }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public Action<string>? ShowMessage { get; set; }

    public Action<string>? OpenFolder { get; set; }

    /// <summary>Shows a Markdown document: title, text.</summary>
    public Action<string, string>? ShowReport { get; set; }

    /// <summary>Raised when a comparison has been built, so the view can bring its tab forward.</summary>
    public event EventHandler? CompareOpened;

    public FleetContext Snapshot() => new(
        "Folder " + Path.GetFileName(_loose.Folder),
        null,
        _loose.Archive,
        [.. Robots.Select(r => (r.Robot, (Core.Controllers.ControllerIdentity?)r.Backups.FirstOrDefault()?.Manifest.Identity))],
        _tool);

    private string Describe()
    {
        int backups = Robots.Sum(r => r.Backups.Count);
        return string.Create(CultureInfo.CurrentCulture, $"{Robots.Count} robot(s), {backups} backup(s), opened as {_loose.Shape}.");
    }

    private void CompareChecked()
    {
        if (Selected is not { } row)
        {
            return;
        }

        List<BackupRowViewModel> ticked = [.. row.Backups.Where(b => b.IsChecked)];
        (BackupRowViewModel a, BackupRowViewModel b) pair;
        if (ticked.Count == 2)
        {
            pair = (ticked[1], ticked[0]);
        }
        else if (ticked.Count == 0 && row.Backups.Count >= 2)
        {
            pair = (row.Backups[1], row.Backups[0]);
        }
        else
        {
            ShowMessage?.Invoke("Tick exactly two backups to compare - or none, to compare the newest with the one before it.");
            return;
        }

        try
        {
            Compare = new CompareViewModel(pair.a.Set, pair.b.Set);
            CompareOpened?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BackupArchiveException)
        {
            ShowMessage?.Invoke($"Could not compare: {ex.Message}");
        }
    }

    private async Task WhatChangedAsync()
    {
        Status = "Comparing each robot's two newest complete backups...";
        List<Core.Robots.Robot> robots = [.. Robots.Select(r => r.Robot)];
        IReadOnlyList<RobotChanges> changes = await Task.Run(() => ChangeReport.ForRobots(_loose.Archive, robots)).ConfigureAwait(true);
        Status = ChangeReport.Headline(changes);
        ShowReport?.Invoke("What changed - " + Path.GetFileName(_loose.Folder),
            ChangeReport.ToMarkdown(Path.GetFileName(_loose.Folder), changes, DateTimeOffset.UtcNow, _tool));
    }
}
