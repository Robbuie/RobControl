using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RobControl.App.Diagnostics;
using RobControl.Core.Backup;
using RobControl.Core.Controllers;
using RobControl.Core.Robots;

namespace RobControl.App.ViewModels;

/// <summary>One robot in the fleet list, and everything the right-hand panes show about it.</summary>
public sealed class RobotRowViewModel : ObservableObject
{
    /// <summary>A complete backup older than this turns the light amber.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);

    private Robot _robot;
    private ControllerIdentity? _identity;
    private DateTimeOffset? _probeUtc;
    private string? _probeSummary;
    private IReadOnlyList<ProbeStep> _probeSteps = [];
    private BackupManifest? _latest;
    private BackupManifest? _latestComplete;
    private bool _isBusy;
    private double _progress;
    private string? _activity;

    public RobotRowViewModel(Robot robot)
    {
        _robot = robot ?? throw new ArgumentNullException(nameof(robot));
    }

    public Robot Robot
    {
        get => _robot;
        set
        {
            if (SetProperty(ref _robot, value))
            {
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(Address));
                OnPropertyChanged(nameof(Line));
            }
        }
    }

    public string Name => Robot.Name;

    public string Address => Robot.Address.ToString();

    public string Line => Robot.Line ?? string.Empty;

    public ObservableCollection<BackupRowViewModel> Backups { get; } = [];

    public ControllerIdentity? Identity
    {
        get => _identity;
        private set
        {
            if (SetProperty(ref _identity, value))
            {
                OnPropertyChanged(nameof(Controller));
            }
        }
    }

    public string Controller => Identity is { IsEmpty: false } id ? id.Describe() : "Not probed yet";

    public string ProbeSummary => _probeSummary is null
        ? "Never probed."
        : string.Create(CultureInfo.CurrentCulture, $"{_probeSummary}  ({_probeUtc?.ToLocalTime():yyyy-MM-dd HH:mm})");

    public IReadOnlyList<ProbeStep> ProbeSteps
    {
        get => _probeSteps;
        private set => SetProperty(ref _probeSteps, value);
    }

    public string LastBackup => _latest is null
        ? "Never"
        : string.Create(CultureInfo.CurrentCulture, $"{_latest.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {_latest.Outcome}");

    public string LastGoodBackup => _latestComplete is null
        ? "No complete backup"
        : string.Create(CultureInfo.CurrentCulture, $"{_latestComplete.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  ({Age(_latestComplete.StartedUtc)})");

    /// <summary>
    /// The light. Green: a complete backup within <see cref="StaleAfter"/>. Amber: the last one was
    /// partial, or the good one is old. Red: the last attempt failed, or the robot does not answer.
    /// Grey: nothing known yet. Never green over something not checked.
    /// </summary>
    public ReadinessState State
    {
        get
        {
            if (_latest is { Outcome: BackupOutcome.Failed } || ProbeSteps.Any(s => s.Name == CapabilityProbe.FtpStep && s.Outcome is ProbeOutcome.Unreachable or ProbeOutcome.Refused))
            {
                return ReadinessState.Blocked;
            }

            if (_latestComplete is null)
            {
                return _latest is null ? ReadinessState.Unknown : ReadinessState.Warning;
            }

            bool stale = DateTimeOffset.UtcNow - _latestComplete.StartedUtc > StaleAfter;
            return stale || _latest?.Outcome != BackupOutcome.Complete ? ReadinessState.Warning : ReadinessState.Ready;
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    /// <summary>0-100 while a backup runs.</summary>
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public string? Activity
    {
        get => _activity;
        set => SetProperty(ref _activity, value);
    }

    public void ApplyProbe(ControllerIdentity? identity, DateTimeOffset? utc, string? summary, IReadOnlyList<ProbeStep>? steps = null)
    {
        Identity = identity;
        _probeUtc = utc;
        _probeSummary = summary;
        if (steps is not null)
        {
            ProbeSteps = steps;
        }

        OnPropertyChanged(nameof(ProbeSummary));
        OnPropertyChanged(nameof(State));
    }

    /// <summary>Replaces the history list from the archive, newest first.</summary>
    public void ApplyBackups(IReadOnlyList<BackupSet> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        Backups.Clear();
        foreach (BackupSet set in sets)
        {
            Backups.Add(new BackupRowViewModel(set));
        }

        _latest = sets.FirstOrDefault()?.Manifest;
        _latestComplete = sets.FirstOrDefault(s => s.Manifest.Outcome == BackupOutcome.Complete)?.Manifest;

        // A backup reads the identity too; fill it in if no probe ever has.
        if ((Identity is null || Identity.IsEmpty) && _latest is { Identity.IsEmpty: false })
        {
            Identity = _latest.Identity;
        }

        OnPropertyChanged(nameof(LastBackup));
        OnPropertyChanged(nameof(LastGoodBackup));
        OnPropertyChanged(nameof(State));
    }

    private static string Age(DateTimeOffset utc)
    {
        TimeSpan age = DateTimeOffset.UtcNow - utc;
        return age.TotalHours < 1 ? "just now"
            : age.TotalDays < 1 ? string.Create(CultureInfo.CurrentCulture, $"{(int)age.TotalHours} h ago")
            : string.Create(CultureInfo.CurrentCulture, $"{(int)age.TotalDays} d ago");
    }
}
