using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobControl.Core;
using RobControl.Core.History;
using RobControl.Core.Insight;

namespace RobControl.App.ViewModels;

/// <summary>
/// The Search tab: text, register and port search across the site's backups, where a program or
/// register is used, and which programs nothing calls. Reads the archive only - no robot is contacted.
/// </summary>
public sealed class SearchViewModel : ObservableObject
{
    public static IReadOnlyList<string> Scopes { get; } = ["Programs (.LS)", "Programs and variables (.LS, .VA)", "All text files"];

    private readonly Func<FleetContext> _context;
    private CancellationTokenSource? _running;
    private string _query = string.Empty;
    private string _scope = Scopes[0];
    private bool _everyBackup;
    private bool _caseSensitive;
    private bool _isRegex;
    private SearchHit? _selectedHit;
    private string _preview = string.Empty;
    private string _status = "Search the newest backup of every robot in this site: a program name, R[45], DO[120], or any text.";
    private string _usesTitle = string.Empty;

    public SearchViewModel(Func<FleetContext> context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy && Query.Trim().Length > 0);
        NotCalledCommand = new AsyncRelayCommand(NotCalledAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public ObservableCollection<SearchHit> Hits { get; } = [];

    /// <summary>Programs that use what was searched for - or, after "Programs nothing calls", those programs.</summary>
    public ObservableCollection<ProgramUse> Uses { get; } = [];

    public IAsyncRelayCommand SearchCommand { get; }

    public IAsyncRelayCommand NotCalledCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? string.Empty))
            {
                SearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Scope
    {
        get => _scope;
        set => SetProperty(ref _scope, value ?? Scopes[0]);
    }

    /// <summary>Every backup of every robot, not only the newest - "when did this line first appear".</summary>
    public bool EveryBackup
    {
        get => _everyBackup;
        set => SetProperty(ref _everyBackup, value);
    }

    public bool CaseSensitive
    {
        get => _caseSensitive;
        set => SetProperty(ref _caseSensitive, value);
    }

    public bool IsRegex
    {
        get => _isRegex;
        set => SetProperty(ref _isRegex, value);
    }

    public bool IsBusy => _running is not null;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string UsesTitle
    {
        get => _usesTitle;
        private set => SetProperty(ref _usesTitle, value);
    }

    public SearchHit? SelectedHit
    {
        get => _selectedHit;
        set
        {
            if (SetProperty(ref _selectedHit, value))
            {
                Preview = value is null ? string.Empty : PreviewOf(value);
            }
        }
    }

    /// <summary>The lines around the selected hit, numbered as in the file, the hit marked with &gt;.</summary>
    public string Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    public void Cancel() => _running?.Cancel();

    private async Task SearchAsync()
    {
        FleetContext context = _context();
        var query = new SearchQuery(Query, ScopeOf(Scope), IsRegex, CaseSensitive);
        bool every = EveryBackup;

        await RunAsync(async token =>
        {
            Status = "Searching...";
            (SearchResult result, IReadOnlyList<ProgramUse> uses, string usesTitle) = await Task.Run(() =>
            {
                IReadOnlyList<RobotBackup> backups = every
                    ? FleetBackups.All(context.Archive, context.RobotList)
                    : FleetBackups.Latest(context.Archive, context.RobotList);
                SearchResult found = BackupSearch.Search(backups, query, cancellation: token);

                // A bare program name or register also gets the where-used view, from the newest backups.
                (IReadOnlyList<ProgramUse> u, string title) = ([], string.Empty);
                string text = query.Text.Trim();
                if (!query.IsRegex && IsIdentifier(text))
                {
                    CrossReference xref = CrossReference.Build(every ? FleetBackups.Latest(context.Archive, context.RobotList) : backups, token);
                    u = xref.WhereUsed(text);
                    title = ProgramListingParser.ReferencePattern(text) is not null
                        ? $"Programs that use {text.ToUpperInvariant()}"
                        : $"Programs that call {text.ToUpperInvariant()} - it exists on {Count(xref.RobotsWith(text).Count, "robot")}";
                }

                return (found, u, title);
            }, token).ConfigureAwait(true);

            Replace(Hits, result.Hits);
            Replace(Uses, uses);
            UsesTitle = usesTitle;
            SelectedHit = Hits.FirstOrDefault();
            Status = result.RobotsSearched == 0
                ? "No complete backups in this site yet - back up first, then search."
                : string.Create(CultureInfo.CurrentCulture,
                    $"{Count(result.Hits.Count, "line")} in {Count(result.FilesSearched, "file")} across {Count(result.RobotsSearched, "robot")}")
                    + (result.Truncated ? $" - stopped at {BackupSearch.DefaultMaxHits}; narrow the search." : ".");
        }).ConfigureAwait(true);
    }

    private async Task NotCalledAsync()
    {
        FleetContext context = _context();
        await RunAsync(async token =>
        {
            Status = "Reading programs...";
            IReadOnlyList<ProgramUse> uses = await Task.Run(
                () => CrossReference.Build(FleetBackups.Latest(context.Archive, context.RobotList), token).NotCalled(), token).ConfigureAwait(true);
            Replace(Uses, uses);
            UsesTitle = "Programs no other program calls - they may still be started by PNS, RSR, a style table, a macro or the PLC";
            Status = string.Create(CultureInfo.CurrentCulture, $"{Count(uses.Count, "program")} not called from another program.");
        }).ConfigureAwait(true);
    }

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        _running = new CancellationTokenSource();
        RaiseBusy();
        try
        {
            await work(_running.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status = "Stopped.";
        }
        catch (RobControlException ex)
        {
            Status = ex.Message + (ex.Remediation is null ? string.Empty : " " + ex.Remediation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not read the archive: {ex.Message}";
        }
        finally
        {
            _running.Dispose();
            _running = null;
            RaiseBusy();
        }
    }

    private void RaiseBusy()
    {
        OnPropertyChanged(nameof(IsBusy));
        SearchCommand.NotifyCanExecuteChanged();
        NotCalledCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private static string PreviewOf(SearchHit hit)
    {
        try
        {
            IReadOnlyList<string> lines = BackupComparer.ReadLines(hit.FullPath);
            int from = Math.Max(0, hit.Line - 7);
            int to = Math.Min(lines.Count, hit.Line + 6);
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture, $"{hit.Robot}  {hit.Stamp}  {hit.File}").AppendLine().AppendLine();
            for (int i = from; i < to; i++)
            {
                text.Append(i + 1 == hit.Line ? "> " : "  ")
                    .Append((i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(5))
                    .Append("  ")
                    .AppendLine(lines[i]);
            }

            return text.ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{hit.FullPath} could not be read: {ex.Message}";
        }
    }

    private static SearchScope ScopeOf(string scope) =>
        Array.IndexOf([.. Scopes], scope) switch
        {
            1 => SearchScope.ProgramsAndVariables,
            2 => SearchScope.AllText,
            _ => SearchScope.Programs,
        };

    private static bool IsIdentifier(string text) =>
        ProgramListingParser.ReferencePattern(text) is not null
        || (text.Length is > 0 and <= 36 && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && char.IsAsciiLetter(text[0]));

    private static string Count(int n, string noun) =>
        string.Create(CultureInfo.CurrentCulture, $"{n} {noun}{(n == 1 ? string.Empty : "s")}");

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (T item in items)
        {
            target.Add(item);
        }
    }
}
