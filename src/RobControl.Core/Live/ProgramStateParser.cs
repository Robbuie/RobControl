using System.Globalization;
using System.Text.RegularExpressions;
using RobControl.Core.Transports.Http;

namespace RobControl.Core.Live;

/// <summary>
/// Reads <c>PRGSTATE.DG</c>. The layout, as seen on an R-30iB, V8.30 RepairTool+ (November 2026):
/// <code>
/// TASK STATES:
///
/// 9     SWAXTSK1 RUNNING @ 874 in PROCESSAMR of SWAXTCMN
///
/// ******  History Data  ******
/// Routine depth: 1  Routine: PROCESSAMR
/// Line:   874       Program: SWAXTCMN                              Type: PC
///
/// 2     DGMONFRC status = ABORTED
/// ...
/// PROGRAM STATES:
/// CHANGE        TP
/// Task: no
/// Lines:   57
/// Comment:         Paper Attch
/// Last modified:   06-NOV-26 14:51
/// </code>
/// A line that fits none of this is skipped, never an error; the page itself is always kept for
/// the Pages tab.
/// </summary>
public static partial class ProgramStateParser
{
    public static ProgramStateReport Parse(string? text)
    {
        string body = DiagnosticPage.Text(text);
        if (body.Length == 0)
        {
            return ProgramStateReport.Empty;
        }

        var tasks = new List<TaskState>();
        var programs = new List<ProgramInfo>();
        Section section = Section.None;

        TaskState? task = null;
        var stack = new List<TaskFrame>();
        (int Depth, string Routine)? pendingFrame = null;

        string? programName = null;
        string? programType = null;
        var fields = new List<KeyValuePair<string, string>>();

        foreach (string raw in body.Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("TASK STATES", StringComparison.OrdinalIgnoreCase))
            {
                section = Section.Tasks;
                continue;
            }

            if (line.TrimStart().StartsWith("PROGRAM STATES", StringComparison.OrdinalIgnoreCase))
            {
                FlushTask();
                section = Section.Programs;
                continue;
            }

            if (section == Section.Tasks)
            {
                Match head = TaskLine().Match(line);
                if (head.Success)
                {
                    FlushTask();
                    string status = head.Groups["status"].Success ? head.Groups["status"].Value : head.Groups["state"].Value;
                    task = new TaskState(
                        int.Parse(head.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture),
                        head.Groups["name"].Value,
                        status.ToUpperInvariant(),
                        head.Groups["line"].Success ? int.Parse(head.Groups["line"].Value, NumberStyles.None, CultureInfo.InvariantCulture) : null,
                        head.Groups["routine"].Success ? head.Groups["routine"].Value : null,
                        head.Groups["prog"].Success ? head.Groups["prog"].Value : null,
                        []);
                    continue;
                }

                Match depth = DepthLine().Match(line);
                if (depth.Success)
                {
                    pendingFrame = (int.Parse(depth.Groups["d"].Value, NumberStyles.None, CultureInfo.InvariantCulture), depth.Groups["r"].Value);
                    continue;
                }

                Match at = FrameLine().Match(line);
                if (at.Success && pendingFrame is { } frame)
                {
                    stack.Add(new TaskFrame(
                        frame.Depth,
                        frame.Routine,
                        at.Groups["p"].Value,
                        int.TryParse(at.Groups["l"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int l) ? l : null,
                        at.Groups["t"].Success ? at.Groups["t"].Value : null));
                    pendingFrame = null;
                }

                continue;
            }

            if (section == Section.Programs)
            {
                if (line.Trim().Length == 0)
                {
                    FlushProgram();
                    continue;
                }

                if (programName is null)
                {
                    Match program = ProgramLine().Match(line);
                    if (program.Success)
                    {
                        programName = program.Groups["name"].Value;
                        programType = program.Groups["type"].Value.ToUpperInvariant();
                    }

                    continue;
                }

                Match field = FieldLine().Match(line);
                if (field.Success)
                {
                    fields.Add(new(field.Groups["k"].Value.Trim(), field.Groups["v"].Value.Trim()));
                }
            }
        }

        FlushTask();
        FlushProgram();
        return new ProgramStateReport(tasks, programs);

        void FlushTask()
        {
            if (task is not null)
            {
                tasks.Add(task with { Stack = [.. stack] });
            }

            task = null;
            stack.Clear();
            pendingFrame = null;
        }

        void FlushProgram()
        {
            if (programName is not null)
            {
                programs.Add(new ProgramInfo(programName, programType ?? string.Empty, [.. fields]));
            }

            programName = null;
            programType = null;
            fields.Clear();
        }
    }

    /// <summary>"06-NOV-26 14:51" as a time; null for "00-0-80 00:00" and anything else that is not one.</summary>
    public static DateTime? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string t = Spaces().Replace(text.Trim(), " ");
        return DateTime.TryParseExact(t, ["dd-MMM-yy HH:mm", "dd-MMM-yy HH:mm:ss", "d-MMM-yy HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when)
            ? when
            : null;
    }

    private enum Section
    {
        None,
        Tasks,
        Programs,
    }

    [GeneratedRegex(@"^\s*(?<n>\d+)\s+(?<name>\S+)\s+(?:status\s*=\s*(?<status>\S+)|(?<state>[A-Za-z_]+)(?:\s*@\s*(?<line>\d+)\s+in\s+(?<routine>\S+)\s+of\s+(?<prog>\S+))?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TaskLine();

    [GeneratedRegex(@"Routine depth:\s*(?<d>\d+)\s+Routine:\s*(?<r>\S+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DepthLine();

    [GeneratedRegex(@"^\s*Line:\s*(?<l>\d+)\s+Program:\s*(?<p>\S+)(?:\s+Type:\s*(?<t>\S+))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FrameLine();

    [GeneratedRegex(@"^(?<name>\S+)\s+(?<type>[A-Za-z]{2,6})\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ProgramLine();

    [GeneratedRegex(@"^(?<k>[A-Za-z][A-Za-z ]{0,30}?):\s*(?<v>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldLine();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}
