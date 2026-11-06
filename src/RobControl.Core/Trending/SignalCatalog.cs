using System.Globalization;
using System.Text.RegularExpressions;
using RobControl.Core.Backup;
using RobControl.Core.History;
using RobControl.Core.Insight;
using RobControl.Core.Transports.Http;

namespace RobControl.Core.Trending;

/// <summary>
/// What there is to trend on a robot, for the Trends tab's signal browser: numeric registers with
/// their comments, I/O with its comments and state, and the system variables that hold a single
/// number.
///
/// <para>Two places to look, both read-only:</para>
/// <list type="bullet">
///   <item><see cref="FromBackup"/> - the robot's newest complete backup on disk. Nothing is sent to the
///   robot, so it works on a laptop away from the line, and it is the only source of system variables:
///   those come from the <c>.VA</c> listings a backup already holds.</item>
///   <item><see cref="ReadAsync"/> - one GET each of <c>/MD/NUMREG.VA</c> and <c>/MD/IOSTATE.DG</c>, the
///   same two pages the recorder reads. Current values and comments; no KCL, no system variables.</item>
/// </list>
///
/// <para><b>Searches rather than parses</b>, like <see cref="RegisterFileParser"/> and
/// <see cref="IoStateParser"/>: the layouts are FANUC's as documented, not yet seen from our own
/// controllers (Phase 0). A line that is not recognised is skipped, never an error, and nothing is
/// listed that <see cref="SignalAddress.TryParse"/> would refuse, so whatever is listed can be added.
/// Safety variables (<c>$DCSS_...</c>) are listed like any other: reading them is a <c>SHOW VAR</c>,
/// and only writing them is refused.</para>
/// </summary>
public static partial class SignalCatalog
{
    /// <summary>A guard against a listing far bigger than any real controller writes.</summary>
    public const int MaxVariables = 20_000;

    /// <summary>
    /// Variables whose elements are already offered another way (registers) or are not single
    /// numbers (position and string registers).
    /// </summary>
    private static readonly string[] SkippedOwners = ["$NUMREG", "$POSREG", "$STRREG"];

    /// <summary>I/O types in the order the pendant's I/O menu lists them.</summary>
    private static readonly List<string> IoOrder = ["DI", "DO", "RI", "RO", "UI", "UO", "SI", "SO", "WI", "WO", "GI", "GO", "AI", "AO", "F", "M"];

    /// <summary>Registers out of a <c>NUMREG.VA</c> listing: <c>[2] = 1287  'Weld count'</c>.</summary>
    public static IReadOnlyList<SignalEntry> Registers(string? text)
    {
        var result = new List<SignalEntry>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        foreach (Match match in RegisterLine().Matches(text))
        {
            if (!int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                || !SignalAddress.TryParse($"R[{n}]", out SignalAddress? address, out _))
            {
                continue;
            }

            result.Add(new SignalEntry(address!, Blank(match.Groups["c"].Value), match.Groups["v"].Value));
        }

        return [.. result.DistinctBy(e => e.Address.Text)];
    }

    /// <summary>I/O out of an <c>IOSTATE.DG</c> page: <c>DI[  1] ON   Part present</c>.</summary>
    public static IReadOnlyList<SignalEntry> Io(string? text)
    {
        var result = new List<SignalEntry>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        foreach (Match match in IoLine().Matches(text))
        {
            string type = match.Groups["type"].Value.ToUpperInvariant();
            if (!SignalAddress.IoTypes.Contains(type)
                || !SignalAddress.TryParse($"{type}[{match.Groups["n"].Value}]", out SignalAddress? address, out _))
            {
                continue;
            }

            // What follows the state on the line is the I/O comment - minus any table markup, in
            // case the page is HTML.
            string comment = Markup().Replace(match.Groups["rest"].Value, " ");
            result.Add(new SignalEntry(address!, Blank(Spaces().Replace(comment, " ")), match.Groups["v"].Value.ToUpperInvariant()));
        }

        return [.. result.DistinctBy(e => e.Address.Text)];
    }

    /// <summary>
    /// System variables that hold one number, out of a <c>.VA</c> listing. A variable named on its
    /// own line (<c>$SCR.$NUM_GROUP  Storage: CMOS  Access: RO  : INTEGER = 1</c>, or a
    /// <c>Field: $X.$Y ... = 1</c> line) is taken as named; a bare <c>[3] = 1.5</c> line is an element
    /// of the variable whose header it sits under.
    /// </summary>
    public static IReadOnlyList<SignalEntry> Variables(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var result = new List<SignalEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string?[] owners = SettingsWatch.Attribute(lines);

        for (int i = 0; i < lines.Count && result.Count < MaxVariables; i++)
        {
            string line = lines[i];
            int equals = line.LastIndexOf('=');
            if (equals < 0)
            {
                continue;
            }

            string value = line[(equals + 1)..].Trim();
            if (value.Length == 0 || value[0] is '\'' or '"' || !ScalarParser.TryParse(value, out _))
            {
                continue;
            }

            string before = line[..equals];
            string? name;
            Match named = VariableOnLine().Match(before);
            if (named.Success)
            {
                name = named.Value;
            }
            else if (Element().Match(before) is { Success: true } element && owners[i] is { } owner)
            {
                name = $"{owner}[{element.Groups["i"].Value.Replace(" ", string.Empty, StringComparison.Ordinal)}]";
            }
            else
            {
                continue;
            }

            name = name.ToUpperInvariant();
            if (SkippedOwners.Any(s => name.StartsWith(s + "[", StringComparison.Ordinal) || name == s)
                || !SignalAddress.TryParse(name, out SignalAddress? address, out _)
                || !seen.Add(address!.Text))
            {
                continue;
            }

            result.Add(new SignalEntry(address, Describe(before), value.Split([' ', '\t'], 2)[0]));
        }

        return result;
    }

    /// <summary>Everything listed in one backup. Reads files on disk only.</summary>
    public static SignalListing FromBackup(BackupSet set, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(set);
        var entries = new List<SignalEntry>();
        var notes = new List<string>();
        IReadOnlyList<BackupFileRecord> files = set.Manifest.Files;

        BackupFileRecord? registerFile = files.FirstOrDefault(f => IsNamed(f, RobotSampler.RegisterFile));
        BackupFileRecord? ioFile = files.FirstOrDefault(f => IsNamed(f, RobotSampler.IoFile));

        if (registerFile is null)
        {
            notes.Add($"No {RobotSampler.RegisterFile} in this backup, so no registers from it.");
        }
        else if (Read(set, registerFile, notes) is { } lines)
        {
            entries.AddRange(Registers(string.Join("\n", lines)));
        }

        if (ioFile is null)
        {
            notes.Add($"No {RobotSampler.IoFile} in this backup, so no I/O from it.");
        }
        else if (Read(set, ioFile, notes) is { } lines)
        {
            entries.AddRange(Io(string.Join("\n", lines)));
        }

        var variables = new List<SignalEntry>();
        foreach (BackupFileRecord file in files.Where(f => Path.GetExtension(f.Name).Equals(".VA", StringComparison.OrdinalIgnoreCase)))
        {
            cancellation.ThrowIfCancellationRequested();
            if (Read(set, file, notes) is { } lines)
            {
                variables.AddRange(Variables(lines));
            }
        }

        entries.AddRange(variables.DistinctBy(e => e.Address.Text).Take(MaxVariables));
        return new SignalListing(Order(entries), notes);
    }

    /// <summary>
    /// Registers and I/O as the robot has them now: one GET of each diagnostic page, through
    /// <paramref name="web"/>, which serialises requests to the robot. Nothing else is asked.
    /// </summary>
    public static async Task<SignalListing> ReadAsync(ControllerWebClient web, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(web);
        var entries = new List<SignalEntry>();
        var notes = new List<string>();

        entries.AddRange(await PageAsync(web, RobotSampler.RegisterFile, Registers, notes, cancellation).ConfigureAwait(false));
        entries.AddRange(await PageAsync(web, RobotSampler.IoFile, Io, notes, cancellation).ConfigureAwait(false));

        return new SignalListing(Order(entries), notes);
    }

    /// <summary>
    /// Several listings as one: the first to list a signal sets its place; a later one fills in a
    /// value or comment the earlier one lacked, and a later value wins (a read of the robot is newer
    /// than its backup).
    /// </summary>
    public static IReadOnlyList<SignalEntry> Merge(IEnumerable<SignalListing> listings)
    {
        ArgumentNullException.ThrowIfNull(listings);
        var merged = new Dictionary<string, SignalEntry>(StringComparer.Ordinal);
        foreach (SignalEntry entry in listings.SelectMany(l => l.Entries))
        {
            merged[entry.Address.Text] = merged.TryGetValue(entry.Address.Text, out SignalEntry? old)
                ? old with { Comment = entry.Comment ?? old.Comment, Value = entry.Value ?? old.Value }
                : entry;
        }

        return Order(merged.Values);
    }

    /// <summary>Registers by number, then I/O by type (in pendant order) and number, then variables by name.</summary>
    private static List<SignalEntry> Order(IEnumerable<SignalEntry> entries)
    {
        return
        [
            .. entries
                .OrderBy(e => e.Address.Kind)
                .ThenBy(e => e.Address.IoType is { } t ? IoOrder.IndexOf(t) : 0)
                .ThenBy(e => e.Address.Index ?? 0)
                .ThenBy(e => e.Address.Text, StringComparer.Ordinal),
        ];
    }

    private static async Task<IReadOnlyList<SignalEntry>> PageAsync(
        ControllerWebClient web, string file, Func<string, IReadOnlyList<SignalEntry>> parse, List<string> notes, CancellationToken cancellation)
    {
        try
        {
            HttpExchange exchange = await web.GetDiagnosticFileAsync(file, cancellation).ConfigureAwait(false);
            if (!exchange.IsSuccess)
            {
                notes.Add($"{file} answered with status {exchange.StatusCode}.");
                return [];
            }

            IReadOnlyList<SignalEntry> found = parse(exchange.Text);
            if (found.Count == 0)
            {
                notes.Add($"{file} arrived but nothing in it was recognised - the layout may not be what was assumed.");
            }

            return found;
        }
        catch (RobControlException ex)
        {
            notes.Add($"{file}: {ex.Message}");
            return [];
        }
    }

    private static IReadOnlyList<string>? Read(BackupSet set, BackupFileRecord file, List<string> notes)
    {
        try
        {
            return BackupComparer.ReadLines(set.PathOf(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BackupArchiveException)
        {
            notes.Add($"Could not read {file.RelativePath}: {ex.Message}");
            return null;
        }
    }

    private static bool IsNamed(BackupFileRecord file, string name) =>
        Path.GetFileName(file.Name.Replace('\\', '/')).Equals(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>"INTEGER, RO" from <c>... Access: RO  : INTEGER</c>, or null.</summary>
    private static string? Describe(string beforeEquals)
    {
        Match type = TypeBeforeEquals().Match(beforeEquals);
        Match access = Access().Match(beforeEquals);
        var parts = new List<string>(2);
        if (type.Success)
        {
            parts.Add(type.Groups["t"].Value.Trim());
        }

        if (access.Success)
        {
            parts.Add(access.Groups["a"].Value.ToUpperInvariant());
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string? Blank(string text)
    {
        string t = text.Trim();
        return t.Length == 0 ? null : t;
    }

    [GeneratedRegex(@"(?m)^\s*\[\s*(?<n>\d{1,6})\s*\]\s*=\s*(?<v>\S+)[ \t]*(?:'(?<c>[^'\r\n]*)')?", RegexOptions.CultureInvariant)]
    private static partial Regex RegisterLine();

    [GeneratedRegex(@"\b(?<type>[A-Za-z]{1,2})\[\s*(?<n>\d{1,6})\s*\]\s*(?:<[^>]{0,80}>\s*){0,6}(?:=\s*)?(?<v>ON|OFF|-?\d+(?:\.\d+)?)\b(?<rest>[^\r\n]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IoLine();

    [GeneratedRegex(@"\$[A-Za-z0-9_]+(?:\[[^\]]*\])?(?:\.\$[A-Za-z0-9_]+(?:\[[^\]]*\])?)*", RegexOptions.CultureInvariant)]
    private static partial Regex VariableOnLine();

    [GeneratedRegex(@"^\s*\[\s*(?<i>\d{1,6}(?:\s*,\s*\d{1,6}){0,2})\s*\]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Element();

    [GeneratedRegex(@":\s*(?<t>[A-Za-z][A-Za-z0-9_ \[\]]{0,40}?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TypeBeforeEquals();

    [GeneratedRegex(@"Access:\s*(?<a>[A-Za-z]{2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex Access();

    [GeneratedRegex(@"<[^>]{0,200}>", RegexOptions.CultureInvariant)]
    private static partial Regex Markup();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}
