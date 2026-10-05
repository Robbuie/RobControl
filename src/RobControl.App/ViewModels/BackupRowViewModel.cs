using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RobControl.App.Diagnostics;
using RobControl.Core.Backup;

namespace RobControl.App.ViewModels;

/// <summary>One backup in a robot's history.</summary>
public sealed class BackupRowViewModel(BackupSet set) : ObservableObject
{
    private bool _isChecked;

    public BackupSet Set { get; } = set ?? throw new ArgumentNullException(nameof(set));

    public BackupManifest Manifest => Set.Manifest;

    public string When => Manifest.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    public string Outcome => Manifest.Outcome.ToString();

    public ReadinessState State => Manifest.Outcome switch
    {
        BackupOutcome.Complete => ReadinessState.Ready,
        BackupOutcome.Partial => ReadinessState.Warning,
        BackupOutcome.Cancelled => ReadinessState.Warning,
        _ => ReadinessState.Blocked,
    };

    public string Files => Manifest.Files.Count.ToString(CultureInfo.CurrentCulture);

    public string Size => Bytes.Describe(Manifest.TotalBytes);

    public string Summary => Manifest.Summary ?? string.Empty;

    public string Controller => Manifest.Identity.Describe();

    public string Folder => Set.FolderPath;

    /// <summary>Ticked to be one side of a comparison.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}
