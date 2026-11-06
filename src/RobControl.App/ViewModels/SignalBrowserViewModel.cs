using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobControl.Core.Trending;

namespace RobControl.App.ViewModels;

/// <summary>
/// The Trends tab's signal browser: everything there is to watch on a robot - registers with their
/// comments, I/O with its comments, system variables that hold a number - to filter, tick and add,
/// instead of typing <c>R[12]</c> from memory.
///
/// <para>It opens on what the robot's newest backup lists, which sends nothing to the robot.
/// <b>Read from robot</b> adds current values with one GET each of the register and I/O pages, the
/// same two the recorder reads. It never reads system variables live - there could be thousands, and
/// each is a KCL request.</para>
///
/// <para>Free of WPF types, like every view model here.</para>
/// </summary>
public sealed class SignalBrowserViewModel : ObservableObject
{
    public const string AllCategory = "All";
    public const string RegistersCategory = "Registers";
    public const string IoCategory = "I/O";
    public const string VariablesCategory = "System variables";

    /// <summary>A filter that matches more than this shows the first ones and says so - a DataGrid of 20,000 rows helps nobody.</summary>
    public const int MaxShown = 1000;

    public static IReadOnlyList<string> Categories { get; } = [AllCategory, RegistersCategory, IoCategory, VariablesCategory];

    private readonly List<SignalListing> _listings = [];
    private readonly IReadOnlySet<string> _watched;
    private readonly Func<CancellationToken, Task<SignalListing>>? _readFromRobot;
    private readonly List<SignalBrowserRowViewModel> _all = [];
    private string _filter = string.Empty;
    private string _category = AllCategory;
    private readonly string _backupSource;
    private string _source;
    private string _notes = string.Empty;
    private string _counts = string.Empty;
    private int _tickedCount;

    /// <param name="robotName">The robot whose signals are listed.</param>
    /// <param name="fromBackup">What its newest backup lists - possibly nothing, with a note saying why.</param>
    /// <param name="source">Where <paramref name="fromBackup"/> came from, for the person: "newest backup, 2026-11-05_141100".</param>
    /// <param name="watched">Signals already on the Trends tab for this robot.</param>
    /// <param name="readFromRobot">One read of the robot, or null when it cannot be read now.</param>
    /// <param name="cannotRead">Why <paramref name="readFromRobot"/> is null - shown on the button's tooltip.</param>
    public SignalBrowserViewModel(
        string robotName,
        SignalListing fromBackup,
        string source,
        IReadOnlySet<string> watched,
        Func<CancellationToken, Task<SignalListing>>? readFromRobot,
        string? cannotRead = null)
    {
        ArgumentNullException.ThrowIfNull(fromBackup);
        RobotName = robotName ?? throw new ArgumentNullException(nameof(robotName));
        _backupSource = source ?? throw new ArgumentNullException(nameof(source));
        _source = source;
        _watched = watched ?? throw new ArgumentNullException(nameof(watched));
        _readFromRobot = readFromRobot;
        ReadFromRobotHint = readFromRobot is null
            ? cannotRead ?? "This robot cannot be read now."
            : "One GET each of NUMREG.VA and IOSTATE.DG for current values and comments. System variables come from the backup only.";

        ReadFromRobotCommand = new AsyncRelayCommand(ReadFromRobotAsync, () => _readFromRobot is not null);
        TickShownCommand = new RelayCommand(() => SetTicks(Shown, true), () => Shown.Count > 0);
        ClearTicksCommand = new RelayCommand(() => SetTicks(_all, false), () => TickedCount > 0);

        _listings.Add(fromBackup);
        Rebuild();
    }

    public string RobotName { get; }

    public string Heading => $"Signals on {RobotName}";

    /// <summary>Where the list came from: the backup, and the robot once it has been read.</summary>
    public string Source
    {
        get => _source;
        private set => SetProperty(ref _source, value);
    }

    /// <summary>Anything found missing or unreadable on the way - one line each.</summary>
    public string Notes
    {
        get => _notes;
        private set
        {
            if (SetProperty(ref _notes, value))
            {
                OnPropertyChanged(nameof(HasNotes));
            }
        }
    }

    public bool HasNotes => Notes.Length > 0;

    public string ReadFromRobotHint { get; }

    /// <summary>Matched against the signal, its kind and its comment, case-insensitively: <c>weld</c>, <c>DO[</c>, <c>$MCR</c>.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public string Category
    {
        get => _category;
        set
        {
            if (SetProperty(ref _category, value ?? AllCategory))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>The rows the filter lets through, up to <see cref="MaxShown"/>.</summary>
    public ObservableCollection<SignalBrowserRowViewModel> Shown { get; } = [];

    /// <summary>"Showing 40 of 3,412 - 5 ticked".</summary>
    public string Counts
    {
        get => _counts;
        private set => SetProperty(ref _counts, value);
    }

    public int TickedCount
    {
        get => _tickedCount;
        private set
        {
            if (SetProperty(ref _tickedCount, value))
            {
                OnPropertyChanged(nameof(CanAdd));
                OnPropertyChanged(nameof(AddText));
                ClearTicksCommand.NotifyCanExecuteChanged();
                UpdateCounts();
            }
        }
    }

    public bool CanAdd => TickedCount > 0;

    public string AddText => TickedCount == 0
        ? "Add"
        : string.Create(CultureInfo.CurrentCulture, $"Add {TickedCount}");

    /// <summary>The ticked signals, in list order, with their comments. What OK adds; one already on the Trends tab is not added twice.</summary>
    public IReadOnlyList<SignalEntry> Ticked => [.. _all.Where(r => r.IsChecked).Select(r => r.Entry)];

    public IAsyncRelayCommand ReadFromRobotCommand { get; }

    public IRelayCommand TickShownCommand { get; }

    public IRelayCommand ClearTicksCommand { get; }

    private async Task ReadFromRobotAsync()
    {
        if (_readFromRobot is null)
        {
            return;
        }

        Source = $"{_backupSource} - reading {RobotName}...";
        SignalListing read;
        try
        {
            read = await _readFromRobot(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is Core.RobControlException or InvalidOperationException)
        {
            read = SignalListing.Empty(ex.Message);
        }

        _listings.Add(read);
        Source = string.Create(CultureInfo.CurrentCulture, $"{_backupSource}, then read from the robot at {DateTime.Now:HH:mm:ss}");
        Rebuild();
    }

    private void Rebuild()
    {
        HashSet<string> ticked = [.. _all.Where(r => r.IsChecked).Select(r => r.Signal)];
        foreach (SignalBrowserRowViewModel row in _all)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        _all.Clear();
        foreach (SignalEntry entry in SignalCatalog.Merge(_listings))
        {
            var row = new SignalBrowserRowViewModel(entry, _watched.Contains(entry.Address.Text))
            {
                IsChecked = ticked.Contains(entry.Address.Text),
            };
            row.PropertyChanged += OnRowChanged;
            _all.Add(row);
        }

        Notes = string.Join(Environment.NewLine, _listings.SelectMany(l => l.Notes).Distinct(StringComparer.Ordinal));
        ApplyFilter();
        TickedCount = _all.Count(r => r.IsChecked);
    }

    private void ApplyFilter()
    {
        string filter = Filter.Trim();
        Shown.Clear();
        foreach (SignalBrowserRowViewModel row in _all.Where(r => InCategory(r) && Matches(r, filter)).Take(MaxShown))
        {
            Shown.Add(row);
        }

        TickShownCommand.NotifyCanExecuteChanged();
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        int matching = _all.Count(r => InCategory(r) && Matches(r, Filter.Trim()));
        string shown = matching > Shown.Count
            ? string.Create(CultureInfo.CurrentCulture, $"Showing the first {Shown.Count:N0} of {matching:N0} - narrow the filter to see the rest")
            : string.Create(CultureInfo.CurrentCulture, $"{matching:N0} of {_all.Count:N0} signals");
        Counts = TickedCount == 0 ? shown : string.Create(CultureInfo.CurrentCulture, $"{shown} - {TickedCount:N0} ticked");
    }

    private bool InCategory(SignalBrowserRowViewModel row) => Category switch
    {
        RegistersCategory => row.Address.Kind == SignalKind.NumericRegister,
        IoCategory => row.Address.Kind == SignalKind.Io,
        VariablesCategory => row.Address.Kind == SignalKind.SystemVariable,
        _ => true,
    };

    private static bool Matches(SignalBrowserRowViewModel row, string filter) =>
        filter.Length == 0
        || row.Signal.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || row.Comment.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || row.Kind.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private void SetTicks(IEnumerable<SignalBrowserRowViewModel> rows, bool value)
    {
        foreach (SignalBrowserRowViewModel row in rows.ToList())
        {
            row.IsChecked = value;
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Counted up and down rather than recounted: Tick all shown flips up to MaxShown rows at once.
        if (e.PropertyName == nameof(SignalBrowserRowViewModel.IsChecked) && sender is SignalBrowserRowViewModel row)
        {
            TickedCount += row.IsChecked ? 1 : -1;
        }
    }
}
