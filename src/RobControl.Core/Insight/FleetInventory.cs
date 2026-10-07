using System.Globalization;
using System.Text;
using RobControl.Core.Backup;
using RobControl.Core.Controllers;
using RobControl.Core.Robots;

namespace RobControl.Core.Insight;

/// <summary>
/// The fleet as a table: model, software, application and serial per robot, from the last probe
/// with the newest backup's record filling any gaps, and where each robot's backups stand.
/// </summary>
public static class FleetInventory
{
    public static IReadOnlyList<InventoryRow> Build(IEnumerable<(Robot Robot, ControllerIdentity? Probed)> robots, BackupArchive archive)
    {
        ArgumentNullException.ThrowIfNull(robots);
        ArgumentNullException.ThrowIfNull(archive);
        var rows = new List<InventoryRow>();

        foreach ((Robot robot, ControllerIdentity? probed) in robots)
        {
            IReadOnlyList<BackupSet> sets;
            try
            {
                sets = archive.List(robot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                sets = [];
            }

            BackupSet? lastComplete = sets.FirstOrDefault(s => s.Manifest.Outcome == BackupOutcome.Complete);
            BackupSet? lastAttempt = sets.FirstOrDefault();
            ControllerIdentity identity = (probed ?? ControllerIdentity.Unknown).Merge(lastAttempt?.Manifest.Identity ?? ControllerIdentity.Unknown);

            rows.Add(new InventoryRow(
                robot.Name,
                robot.Address.ToString(),
                robot.Line,
                identity.Generation == ControllerGeneration.Unknown ? string.Empty : ControllerIdentity.Name(identity.Generation),
                identity.RobotModel,
                identity.SoftwareVersion,
                identity.Application,
                identity.FNumber,
                lastComplete?.Manifest.StartedUtc,
                lastAttempt?.Manifest.StartedUtc,
                lastAttempt is null ? "Never backed up" : lastAttempt.Manifest.Outcome.ToString(),
                sets.Count));
        }

        return rows;
    }

    /// <summary>Comma-separated, quoted where needed, dates in ISO form - opens cleanly in Excel in any locale.</summary>
    public static string ToCsv(IEnumerable<InventoryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var csv = new StringBuilder();
        csv.AppendLine("Robot,Address,Line,Controller,Model,Software,Application,F-number,Last complete backup,Last backup attempt,Last outcome,Backups");
        foreach (InventoryRow r in rows)
        {
            csv.AppendLine(string.Join(",",
                Quote(r.Robot), Quote(r.Address), Quote(r.Line), Quote(r.Controller), Quote(r.Model), Quote(r.Software),
                Quote(r.Application), Quote(r.FNumber), Quote(Time(r.LastCompleteUtc)), Quote(Time(r.LastAttemptUtc)),
                Quote(r.LastOutcome), r.BackupCount.ToString(CultureInfo.InvariantCulture)));
        }

        return csv.ToString();
    }

    private static string Time(DateTimeOffset? utc) =>
        utc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// A CSV field. A leading = + - @ is prefixed with a quote mark so a robot name from a site file
    /// can never become a spreadsheet formula.
    /// </summary>
    internal static string Quote(string? value)
    {
        string text = value ?? string.Empty;
        if (text.Length > 0 && "=+-@".Contains(text[0], StringComparison.Ordinal))
        {
            text = "'" + text;
        }

        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 || text.StartsWith('\'')
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }
}
