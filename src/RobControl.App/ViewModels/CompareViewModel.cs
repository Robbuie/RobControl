using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RobControl.Core.Backup;
using RobControl.Core.History;

namespace RobControl.App.ViewModels;

/// <summary>
/// Two backups of one robot, side by side: which files changed, and for a selected text file, which
/// lines - as hunks with context, the way a unified diff reads, rather than the whole file.
/// </summary>
public sealed class CompareViewModel : ObservableObject
{
    private readonly IReadOnlyList<FileComparison> _all;
    private bool _hideUnchanged = true;
    private bool _hideLiveData = true;
    private FileRowViewModel? _selectedFile;

    public CompareViewModel(BackupSet older, BackupSet newer)
    {
        ArgumentNullException.ThrowIfNull(older);
        ArgumentNullException.ThrowIfNull(newer);

        // Older first whatever order they were ticked in, so "removed" means removed.
        (Older, Newer) = older.Manifest.StartedUtc <= newer.Manifest.StartedUtc ? (older, newer) : (newer, older);
        _all = BackupComparer.Compare(Older, Newer);
        Refilter();
    }

    public BackupSet Older { get; }

    public BackupSet Newer { get; }

    public string Title => string.Create(CultureInfo.CurrentCulture,
        $"{Older.Manifest.RobotName}: {Stamp(Older)}  ->  {Stamp(Newer)}");

    public string Summary
    {
        get
        {
            int changed = _all.Count(f => f.Change == FileChange.Changed && !f.IsLiveData);
            int added = _all.Count(f => f.Change == FileChange.Added);
            int removed = _all.Count(f => f.Change == FileChange.Removed);
            int live = _all.Count(f => f.Change == FileChange.Changed && f.IsLiveData);
            return changed + added + removed == 0
                ? string.Create(CultureInfo.CurrentCulture, $"No program or variable changes. {live} live-data files differ, as they always do.")
                : string.Create(CultureInfo.CurrentCulture, $"{changed} changed, {added} added, {removed} removed  (plus {live} live-data files)");
        }
    }

    public ObservableCollection<FileRowViewModel> Files { get; } = [];

    public ObservableCollection<DiffLineViewModel> Lines { get; } = [];

    public bool HideUnchanged
    {
        get => _hideUnchanged;
        set
        {
            if (SetProperty(ref _hideUnchanged, value))
            {
                Refilter();
            }
        }
    }

    /// <summary>Hides <c>.DG</c> snapshots, which differ between any two backups by nature.</summary>
    public bool HideLiveData
    {
        get => _hideLiveData;
        set
        {
            if (SetProperty(ref _hideLiveData, value))
            {
                Refilter();
            }
        }
    }

    public FileRowViewModel? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetProperty(ref _selectedFile, value))
            {
                LoadLines();
            }
        }
    }

    private void Refilter()
    {
        Files.Clear();
        foreach (FileComparison file in _all)
        {
            if (HideUnchanged && file.Change == FileChange.Same)
            {
                continue;
            }

            if (HideLiveData && file.IsLiveData)
            {
                continue;
            }

            Files.Add(new FileRowViewModel(file));
        }

        SelectedFile = Files.FirstOrDefault(f => f.Comparison.CanDiff) ?? Files.FirstOrDefault();
    }

    private void LoadLines()
    {
        Lines.Clear();
        if (SelectedFile is not { } row)
        {
            return;
        }

        FileComparison file = row.Comparison;
        switch (file.Change)
        {
            case FileChange.Same:
                Lines.Add(DiffLineViewModel.Note("Identical in both backups (same SHA-256)."));
                return;
            case FileChange.Added:
                Lines.Add(DiffLineViewModel.Note("Only in the newer backup."));
                break;
            case FileChange.Removed:
                Lines.Add(DiffLineViewModel.Note("Only in the older backup."));
                break;
        }

        bool text = file.Change switch
        {
            FileChange.Changed => file.CanDiff,
            FileChange.Added => TextSniffer.LooksLikeText(Newer.PathOf(file.New!)),
            _ => TextSniffer.LooksLikeText(Older.PathOf(file.Old!)),
        };

        if (!text)
        {
            Lines.Add(DiffLineViewModel.Note("Binary file: compared by hash only. The ASCII listing beside it (.LS, .VA) shows what changed."));
            return;
        }

        IReadOnlyList<DiffLine> diff = BackupComparer.DiffFile(Older, Newer, file);
        foreach (DiffHunk hunk in LineDiff.Hunks(diff))
        {
            Lines.Add(DiffLineViewModel.Header(hunk));
            foreach (DiffLine line in hunk.Lines)
            {
                Lines.Add(DiffLineViewModel.From(line));
            }
        }
    }

    private static string Stamp(BackupSet set) =>
        set.Manifest.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
}
