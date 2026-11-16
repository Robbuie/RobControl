using RobControl.Core.Trending;

namespace RobControl.Core.Live;

/// <summary>
/// "Why won't it run?" - one Live read turned into a list of reasons: active alarms, stop signals that
/// are TRUE, the UOP inputs that hold a robot, the UOP outputs that say why it will not take a start,
/// and tasks that are paused.
///
/// <para><b>Read-only, and from pages already read.</b> Nothing extra is asked of the controller; this
/// is the Live read looked at from the "it stopped" side.</para>
///
/// <para><b>UOP by the standard assignment</b> (UI[1] *IMSTP ... UI[8] ENBL, UO[1] CMDENBL ... UO[8]
/// TPENBL), as in FANUC's manuals. A controller with UOP disabled or remapped reads differently - when
/// every UI is OFF the rules are not applied and a note says why. The comment the controller lists for
/// each point is shown beside it, so a remapped one is visible.</para>
/// </summary>
public static class RunCheck
{
    private static readonly (int Index, string Name, BlockerLevel Level, string Why, string Where)[] Inputs =
    [
        (1, "*IMSTP", BlockerLevel.Stop, "Immediate stop input is off - the cell or PLC is stopping the robot (servo power off).", "The PLC's *IMSTP output, or the cell's E-stop chain into UI[1]."),
        (2, "*HOLD", BlockerLevel.Hold, "Hold input is off - the robot decelerates and holds.", "The PLC's hold output, or the cell's hold circuit into UI[2]."),
        (3, "*SFSPD", BlockerLevel.Hold, "Safety speed input is off - typically the fence or a safety PLC output; the robot pauses or runs at reduced speed.", "Fence door / safety PLC output into UI[3]."),
        (8, "ENBL", BlockerLevel.Hold, "Enable input is off - the robot will not take a remote start.", "The PLC's enable output into UI[8]."),
    ];

    private static readonly (int Index, string Name, bool BadWhenOn, BlockerLevel Level, string Why, string Where)[] Outputs =
    [
        (1, "CMDENBL", false, BlockerLevel.Hold, "Command enable is off - the robot will not accept a remote start (not in remote, a fault, or the TP has control).", "Remote/Local on the pendant (Setup > Prog Select / System Config), alarms, the TP enable switch."),
        (2, "SYSRDY", false, BlockerLevel.Hold, "System ready is off - servo power is off.", "Alarms, E-stops, the fence, *IMSTP."),
        (4, "PAUSED", true, BlockerLevel.Hold, "A program is paused.", "The Program tab: which task, and where."),
        (5, "HELD", true, BlockerLevel.Hold, "The robot is held.", "*HOLD (UI[2]) or the pendant's HOLD key."),
        (6, "FAULT", true, BlockerLevel.Stop, "Fault output is on - an alarm has stopped the robot.", "Active alarms above; RESET once the cause is cleared."),
        (8, "TPENBL", true, BlockerLevel.Hold, "The teach pendant is enabled - it has control, so nothing remote will start the robot.", "The TP's ON/OFF enable switch."),
    ];

    public static IReadOnlyList<RunBlocker> Evaluate(LiveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var found = new List<RunBlocker>();

        foreach (Insight.AlarmEntry alarm in snapshot.ActiveAlarms)
        {
            found.Add(new RunBlocker(BlockerLevel.Stop, $"Active alarm {alarm.Code}", alarm.Message,
                "Alarm > Active on the pendant. Clear the cause, then RESET."));
        }

        foreach (SafetySignal signal in snapshot.SafetySignals.Where(s => s.IsOn))
        {
            found.Add(new RunBlocker(BlockerLevel.Stop, $"{signal.Name} is TRUE", "The controller's stop signals page reports it as active.",
                "The circuit it names: E-stop chain, fence, deadman, hand-broken input."));
        }

        Dictionary<string, IoPoint> io = snapshot.IoPoints.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        List<IoPoint> ui = [.. snapshot.IoPoints.Where(p => p.Type == "UI")];
        if (ui.Count > 0 && ui.All(p => !p.IsOn))
        {
            found.Add(new RunBlocker(BlockerLevel.Note, "Every UI input is OFF",
                "That usually means UOP is not used on this robot (disabled, or the cell starts it from the pendant), not that every input is really low - so the UOP checks are not applied.",
                "System > Config: Enable UI signals; I/O > UOP for the assignment."));
        }
        else if (ui.Count > 0)
        {
            foreach ((int index, string name, BlockerLevel level, string why, string where) in Inputs)
            {
                if (io.TryGetValue($"UI[{index}]", out IoPoint? point) && !point.IsOn)
                {
                    found.Add(new RunBlocker(level, $"UI[{index}] {Label(point, name)} is OFF", why, where));
                }
            }
        }

        foreach ((int index, string name, bool badWhenOn, BlockerLevel level, string why, string where) in Outputs)
        {
            if (io.TryGetValue($"UO[{index}]", out IoPoint? point) && point.IsOn == badWhenOn)
            {
                found.Add(new RunBlocker(level, $"UO[{index}] {Label(point, name)} is {(point.IsOn ? "ON" : "OFF")}", why, where));
            }
        }

        foreach (TaskState task in snapshot.Programs.Tasks.Where(t => t.Status.Contains("PAUSE", StringComparison.OrdinalIgnoreCase)))
        {
            found.Add(new RunBlocker(BlockerLevel.Hold, $"Task {task.Name} is paused", task.Where.Length > 0 ? $"Paused at {task.Where}." : "Paused.",
                "The pendant's program screen; Where is it waiting? below for the line."));
        }

        if (snapshot.Programs.Tasks.Count > 0 && !snapshot.Programs.Tasks.Any(t => t.IsRunning))
        {
            found.Add(new RunBlocker(BlockerLevel.Note, "No task is running", "Program state lists no running task.", "The Program tab."));
        }

        return [.. found.OrderBy(b => b.Level)];
    }

    /// <summary>"*HOLD" - or "*HOLD (listed as 'Cell hold')" when the controller's comment says something else.</summary>
    private static string Label(IoPoint point, string standard) =>
        point.Comment.Length == 0 || point.Comment.Contains(standard.TrimStart('*'), StringComparison.OrdinalIgnoreCase)
            ? standard
            : $"{standard} (listed as '{point.Comment}')";
}
