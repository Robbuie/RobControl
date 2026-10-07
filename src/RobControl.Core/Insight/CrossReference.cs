using RobControl.Core.Backup;
using RobControl.Core.History;

namespace RobControl.Core.Insight;

/// <summary>
/// Every TP program in a set of backups, and what refers to what: who calls a program, which
/// programs use a register or port, and which programs nothing calls.
/// </summary>
public sealed class CrossReference
{
    private readonly Dictionary<string, IReadOnlyList<ProgramListing>> _byRobot;

    private CrossReference(Dictionary<string, IReadOnlyList<ProgramListing>> byRobot) => _byRobot = byRobot;

    public IReadOnlyCollection<string> Robots => _byRobot.Keys;

    public int ProgramCount => _byRobot.Values.Sum(p => p.Count);

    public IReadOnlyList<ProgramListing> ProgramsOn(string robot) =>
        _byRobot.TryGetValue(robot, out IReadOnlyList<ProgramListing>? programs) ? programs : [];

    public static CrossReference Build(IEnumerable<RobotBackup> backups, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(backups);
        var byRobot = new Dictionary<string, IReadOnlyList<ProgramListing>>(StringComparer.OrdinalIgnoreCase);
        foreach (RobotBackup backup in backups)
        {
            var programs = new List<ProgramListing>();
            foreach (BackupFileRecord file in backup.Set.Manifest.Files.Where(f => Path.GetExtension(f.Name).Equals(".LS", StringComparison.OrdinalIgnoreCase)))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if (ProgramListingParser.Parse(BackupComparer.ReadLines(backup.Set.PathOf(file))) is { } listing)
                    {
                        programs.Add(listing);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BackupArchiveException)
                {
                }
            }

            byRobot[backup.Robot] = programs;
        }

        return new CrossReference(byRobot);
    }

    /// <summary>Programs that CALL or RUN <paramref name="program"/>, on every robot.</summary>
    public IReadOnlyList<ProgramUse> Callers(string program) => Uses(ReferenceKind.Program, program.Trim().ToUpperInvariant());

    /// <summary>
    /// Programs that use a register or port - <c>R[45]</c>, <c>DO[120]</c> - or call a program by that
    /// name. Empty when <paramref name="target"/> is neither.
    /// </summary>
    public IReadOnlyList<ProgramUse> WhereUsed(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        string trimmed = target.Trim();
        if (ProgramListingParser.ReferencePattern(trimmed) is not null)
        {
            ProgramReference? canonical = ProgramListingParser.ReferencesIn(trimmed, 0).FirstOrDefault();
            return canonical is null ? [] : Uses(canonical.Kind, canonical.Target);
        }

        return Callers(trimmed);
    }

    /// <summary>The robots that have a program called <paramref name="program"/>.</summary>
    public IReadOnlyList<string> RobotsWith(string program) =>
        [.. _byRobot.Where(p => p.Value.Any(l => string.Equals(l.Name, program.Trim(), StringComparison.OrdinalIgnoreCase))).Select(p => p.Key).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Programs no other program on the same robot CALLs or RUNs. Not necessarily dead: a program can
    /// be started by PNS, RSR, a style table, a macro or the PLC - which is exactly why this is a list
    /// to check, never something to act on blindly.
    /// </summary>
    public IReadOnlyList<ProgramUse> NotCalled()
    {
        var result = new List<ProgramUse>();
        foreach ((string robot, IReadOnlyList<ProgramListing> programs) in _byRobot.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var called = new HashSet<string>(programs.SelectMany(p => p.Calls.Where(c => !string.Equals(c, p.Name, StringComparison.OrdinalIgnoreCase))), StringComparer.OrdinalIgnoreCase);
            result.AddRange(programs.Where(p => !called.Contains(p.Name)).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => new ProgramUse(robot, p.Name, p.Comment, [])));
        }

        return result;
    }

    private List<ProgramUse> Uses(ReferenceKind kind, string target)
    {
        var result = new List<ProgramUse>();
        foreach ((string robot, IReadOnlyList<ProgramListing> programs) in _byRobot.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (ProgramListing program in programs.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                List<int> lines = [.. program.References
                    .Where(r => r.Kind == kind && string.Equals(r.Target, target, StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.Line).Distinct()];
                if (lines.Count > 0)
                {
                    result.Add(new ProgramUse(robot, program.Name, program.Comment, lines));
                }
            }
        }

        return result;
    }
}
