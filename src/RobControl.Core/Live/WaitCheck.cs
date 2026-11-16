using System.Globalization;
using System.Text.RegularExpressions;
using RobControl.Core.Insight;
using RobControl.Core.Trending;

namespace RobControl.Core.Live;

/// <summary>
/// "Where is it waiting?" - each task's program and line from <c>PRGSTATE.DG</c>, that line out of the
/// program's listing in the robot's newest backup, and the live value of every I/O point it names: a
/// task at <c>WAIT DI[34:Part present]=ON</c> with DI[34] OFF is waiting on DI[34].
///
/// <para><b>The line comes from the backup, not the robot.</b> A program edited since the backup can
/// have moved its lines; when program state says it was modified after the backup was taken, the line
/// is shown with a note saying so. Nothing extra is read from the controller.</para>
/// </summary>
public static partial class WaitCheck
{
    /// <param name="listing">The program's listing from the newest backup, and when that backup was taken (UTC); null when there is none.</param>
    public static IReadOnlyList<WaitLine> Evaluate(LiveSnapshot snapshot, Func<string, (ProgramListing Listing, DateTimeOffset TakenUtc)?> listing)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(listing);
        Dictionary<string, IoPoint> io = snapshot.IoPoints.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var lines = new List<WaitLine>();

        foreach (TaskState task in snapshot.Programs.Tasks.Where(t => !t.Status.Contains("ABORT", StringComparison.OrdinalIgnoreCase)))
        {
            (string? program, int? line) = Position(task);
            if (program is null)
            {
                continue;
            }

            ProgramInfo? info = snapshot.Programs.Programs.FirstOrDefault(p => p.Name.Equals(program, StringComparison.OrdinalIgnoreCase));
            if (info is not null && !info.Type.Equals("TP", StringComparison.OrdinalIgnoreCase) && !info.Type.Equals("MACRO", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(new WaitLine(task.Name, task.Status, program, line, null, [], $"A {info.Type} program - only TP programs have a listing to show."));
                continue;
            }

            if (line is null)
            {
                lines.Add(new WaitLine(task.Name, task.Status, program, null, null, [], "Program state does not give a line."));
                continue;
            }

            if (listing(program) is not { } found)
            {
                lines.Add(new WaitLine(task.Name, task.Status, program, line, null, [], $"No {program}.LS in this robot's newest complete backup - back it up to see the line."));
                continue;
            }

            ProgramLine? at = found.Listing.Lines.FirstOrDefault(l => l.Number == line);
            string? note = null;
            if (info?.ModifiedTime is { } modified && modified > found.TakenUtc.ToLocalTime().DateTime)
            {
                note = string.Create(CultureInfo.InvariantCulture,
                    $"{program} was modified on the robot ({modified:yyyy-MM-dd HH:mm}) after the backup this line comes from ({found.TakenUtc.ToLocalTime():yyyy-MM-dd HH:mm}) - the line may have moved.");
            }

            if (at is null)
            {
                lines.Add(new WaitLine(task.Name, task.Status, program, line, null, [], note ?? $"The listing in the backup has no line {line}."));
                continue;
            }

            lines.Add(new WaitLine(task.Name, task.Status, program, line, at.Text, Signals(at.Text, io), note));
        }

        return lines;
    }

    /// <summary>The routine and line the task is at: its status line, or the deepest frame of its stack.</summary>
    private static (string? Program, int? Line) Position(TaskState task)
    {
        if (task.Routine is { Length: > 0 } routine)
        {
            return (routine, task.Line);
        }

        TaskFrame? deepest = task.Stack.OrderByDescending(f => f.Depth).FirstOrDefault();
        return deepest is null ? (null, null) : (deepest.Routine, deepest.Line);
    }

    /// <summary>Every I/O point on the line, with the value it is compared to when the line compares it.</summary>
    internal static IReadOnlyList<WaitSignal> Signals(string text, IReadOnlyDictionary<string, IoPoint> io)
    {
        var signals = new List<WaitSignal>();
        foreach (ProgramReference reference in ProgramListingParser.ReferencesIn(text, 0).Where(r => r.Kind == ReferenceKind.Io).DistinctBy(r => r.Target))
        {
            string? expected = null;
            Regex? pattern = ProgramListingParser.ReferencePattern(reference.Target);
            Match? match = pattern?.Match(text);
            if (match is { Success: true })
            {
                Match compare = Comparison().Match(text, match.Index + match.Length);
                if (compare.Success && compare.Index == match.Index + match.Length)
                {
                    expected = (compare.Groups["op"].Value == "<>" ? "not " : string.Empty) + compare.Groups["v"].Value.ToUpperInvariant();
                }
            }

            io.TryGetValue(reference.Target, out IoPoint? point);
            string? now = point?.State;
            bool? satisfied = expected is null || now is null ? null : Meets(expected, point!);
            signals.Add(new WaitSignal(reference.Target, point?.Comment ?? string.Empty, expected, now, satisfied));
        }

        return signals;
    }

    private static bool? Meets(string expected, IoPoint point)
    {
        bool negate = expected.StartsWith("not ", StringComparison.Ordinal);
        string want = negate ? expected[4..] : expected;
        bool? equal = want switch
        {
            "ON" => point.IsDigital ? point.IsOn : null,
            "OFF" => point.IsDigital ? !point.IsOn : null,
            _ => double.TryParse(want, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? point.Value.Equals(n) : null,
        };
        return equal is null ? null : equal.Value != negate;
    }

    [GeneratedRegex(@"\G\s*(?<op>=|<>)\s*(?<v>ON|OFF|-?\d+(?:\.\d+)?)(?![A-Za-z0-9_\[])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Comparison();
}
