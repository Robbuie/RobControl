using System.Collections.ObjectModel;
using System.Globalization;
// UseWPF drops System.IO from the implicit usings.
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobControl.Core.Live;

namespace RobControl.App.ViewModels;

/// <summary>
/// Alarms > Snapshots: the state each robot was in when a new alarm appeared while it was being recorded
/// (Trends > Start with "Snapshot on new alarm"). Read from the snapshot folders; nothing is sent to a robot.
/// </summary>
public sealed class SnapshotsViewModel : ObservableObject
{
    private readonly Func<string> _folder;
    private AlarmSnapshot? _selected;
    private string _text = string.Empty;
    private string _status = string.Empty;

    public SnapshotsViewModel(Func<string> folder)
    {
        _folder = folder ?? throw new ArgumentNullException(nameof(folder));
        RefreshCommand = new RelayCommand(Refresh);
        OpenFolderCommand = new RelayCommand(() => OpenFolder?.Invoke(Selected?.Folder ?? Root()));
        ShowCommand = new RelayCommand(() =>
        {
            if (Selected is { } s)
            {
                ShowReport?.Invoke($"Alarm snapshot - {s.Robot} {s.When}", _text);
            }
        }, () => Selected is not null);
    }

    public Action<string>? OpenFolder { get; set; }

    /// <summary>Shows a Markdown document: title, text.</summary>
    public Action<string, string>? ShowReport { get; set; }

    public IRelayCommand RefreshCommand { get; }

    public IRelayCommand OpenFolderCommand { get; }

    public IRelayCommand ShowCommand { get; }

    public ObservableCollection<AlarmSnapshot> Snapshots { get; } = [];

    public AlarmSnapshot? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                Text = Read(value);
                ShowCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>The selected snapshot's snapshot.md.</summary>
    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public void Refresh()
    {
        string? keep = Selected?.Folder;
        Snapshots.Clear();
        try
        {
            foreach (AlarmSnapshot snapshot in AlarmSnapshots.List(Root()))
            {
                Snapshots.Add(snapshot);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not read {Root()}: {ex.Message}";
            return;
        }

        Selected = Snapshots.FirstOrDefault(s => s.Folder == keep) ?? Snapshots.FirstOrDefault();
        Status = Snapshots.Count == 0
            ? "No snapshots yet. On the Trends tab, tick Snapshot on new alarm and Start: a new alarm on a recorded robot saves its Live pages here."
            : string.Create(CultureInfo.CurrentCulture, $"{Snapshots.Count} snapshot(s) in {Root()}.");
    }

    private string Root() => _folder();

    private static string Read(AlarmSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return string.Empty;
        }

        try
        {
            return File.ReadAllText(Path.Combine(snapshot.Folder, AlarmSnapshots.MarkdownFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not read the snapshot: {ex.Message}";
        }
    }
}
