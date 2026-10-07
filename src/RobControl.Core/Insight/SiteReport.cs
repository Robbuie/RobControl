using System.Globalization;
using System.Net;
using System.Text;

namespace RobControl.Core.Insight;

/// <summary>
/// The site visit report: one self-contained HTML page - backup status, alarms, watched setting
/// changes and notes - that opens in any browser and prints, or saves as PDF, from there.
///
/// <para>HTML rather than PDF because it needs no package, and every browser on a plant laptop
/// already prints to PDF. <b>Everything in it is encoded</b>: robot names, alarm text and variable
/// lines came from controllers and site files, and none of it may become markup or script.</para>
/// </summary>
public static class SiteReport
{
    public static string Render(SiteReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        CultureInfo culture = CultureInfo.CurrentCulture;
        var html = new StringBuilder();
        string since = data.Since.ToLocalTime().ToString("d", culture);
        string generated = data.GeneratedUtc.ToLocalTime().ToString("g", culture);

        html.Append("""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            """);
        html.Append(CultureInfo.InvariantCulture, $"<title>{E(data.SiteName)} - RobControl site report</title>");
        html.Append("""
            <style>
              body { font: 13px/1.45 "Segoe UI", system-ui, sans-serif; color: #1d2329; margin: 28px auto; max-width: 1100px; padding: 0 16px; }
              h1 { font-size: 22px; margin: 0 0 2px; } h2 { font-size: 16px; margin: 26px 0 8px; border-bottom: 1px solid #d5dae0; padding-bottom: 4px; }
              .muted { color: #66707a; } .notes { white-space: pre-wrap; background: #f4f6f8; padding: 8px 10px; border-radius: 4px; }
              table { border-collapse: collapse; width: 100%; margin: 4px 0 8px; }
              th, td { text-align: left; padding: 4px 8px; border-bottom: 1px solid #e3e7eb; vertical-align: top; }
              th { background: #f4f6f8; font-weight: 600; } td.n { text-align: right; font-variant-numeric: tabular-nums; }
              code { font: 12px Consolas, monospace; } .bad { color: #b3261e; font-weight: 600; } .warn { color: #9a6200; font-weight: 600; } .ok { color: #2e7d32; }
              .tiles { display: flex; gap: 12px; flex-wrap: wrap; margin: 14px 0; }
              .tile { border: 1px solid #d5dae0; border-radius: 6px; padding: 8px 14px; min-width: 130px; }
              .tile b { display: block; font-size: 20px; }
              @media print { body { margin: 0; } h2 { break-after: avoid; } tr { break-inside: avoid; } }
            </style></head><body>
            """);

        html.Append(CultureInfo.InvariantCulture, $"<h1>{E(data.SiteName)}</h1>");
        html.Append(CultureInfo.InvariantCulture, $"<div class=\"muted\">Site report - {E(generated)} - alarms and setting changes since {E(since)} - {E(data.Tool)}</div>");

        int robots = data.Inventory.Count;
        int stale = data.Inventory.Count(r => r.BackupAgeDays(data.GeneratedUtc) is not { } d || d > data.StaleAfterDays);
        int failing = data.Inventory.Count(r => r.Health(data.GeneratedUtc, data.StaleAfterDays) == BackupHealth.Failing);
        int alarms = data.RobotAlarms.Sum(r => r.Count);
        html.Append("<div class=\"tiles\">");
        Tile(html, robots.ToString(culture), "robots");
        Tile(html, stale.ToString(culture), $"without a complete backup in {data.StaleAfterDays} days", stale > 0 ? "bad" : "ok");
        if (failing > 0)
        {
            Tile(html, failing.ToString(culture), "whose latest backup attempts failed", "warn");
        }

        Tile(html, alarms.ToString(culture), "alarms in the period");
        Tile(html, data.ConcernAlarms.Count.ToString(culture), "battery / collision / mastering alarms", data.ConcernAlarms.Count > 0 ? "warn" : "ok");
        Tile(html, data.SettingChanges.Count.ToString(culture), "watched setting changes", data.SettingChanges.Count > 0 ? "warn" : "ok");
        html.Append("</div>");

        if (!string.IsNullOrWhiteSpace(data.SiteNotes))
        {
            html.Append(CultureInfo.InvariantCulture, $"<h2>Site notes</h2><div class=\"notes\">{E(data.SiteNotes)}</div>");
        }

        html.Append("<h2>Robots and backups</h2>");
        if (robots == 0)
        {
            html.Append("<p class=\"muted\">No robots in this site.</p>");
        }
        else
        {
            html.Append("<table><tr><th>Robot</th><th>Address</th><th>Line</th><th>Controller</th><th>Model</th><th>Software</th><th>Last complete backup</th><th>Age</th><th>Backups</th></tr>");
            foreach (InventoryRow r in data.Inventory)
            {
                double? age = r.BackupAgeDays(data.GeneratedUtc);
                string ageClass = age is not { } a || a > data.StaleAfterDays ? "bad" : "ok";
                string ageText = age is { } days ? (days < 1 ? "today" : string.Create(culture, $"{days:0} d")) : "none";
                string healthClass = r.Health(data.GeneratedUtc, data.StaleAfterDays) switch
                {
                    BackupHealth.Ok => "ok",
                    BackupHealth.Failing => "warn",
                    _ => "bad",
                };
                html.Append(CultureInfo.InvariantCulture,
                    $"<tr><td>{E(r.Robot)}</td><td><code>{E(r.Address)}</code></td><td>{E(r.Line)}</td><td>{E(r.Controller)}</td><td>{E(r.Model)}</td><td>{E(r.Software)}</td>"
                    + $"<td>{E(When(r.LastCompleteUtc, culture))}</td><td class=\"{ageClass}\">{E(ageText)}</td><td class=\"{healthClass}\">{E(r.HealthText(data.GeneratedUtc, data.StaleAfterDays))}</td></tr>");
            }

            html.Append("</table>");
        }

        html.Append("<h2>Alarms needing a job</h2>");
        if (data.ConcernAlarms.Count == 0)
        {
            html.Append("<p class=\"ok\">No battery, collision or mastering alarms in the period.</p>");
        }
        else
        {
            html.Append("<table><tr><th>When</th><th>Robot</th><th>Kind</th><th>Alarm</th></tr>");
            foreach (AlarmEntry a in data.ConcernAlarms)
            {
                html.Append(CultureInfo.InvariantCulture,
                    $"<tr><td>{E(a.When?.ToString("g", culture))}</td><td>{E(a.Robot)}</td><td class=\"warn\">{E(AlarmConcerns.Describe(a.Concern))}</td><td><code>{E(a.Code)}</code> {E(a.Message)}</td></tr>");
            }

            html.Append("</table>");
        }

        html.Append("<h2>Most frequent alarms</h2>");
        if (data.AlarmCodes.Count == 0)
        {
            html.Append("<p class=\"muted\">No alarm logs in the backups for the period.</p>");
        }
        else
        {
            html.Append("<table><tr><th>Alarm</th><th>Message</th><th class=\"n\">Count</th><th>Robots</th><th>Last seen</th></tr>");
            foreach (AlarmCodeSummary c in data.AlarmCodes.Take(15))
            {
                html.Append(CultureInfo.InvariantCulture,
                    $"<tr><td><code>{E(c.Code)}</code></td><td>{E(c.Message)}</td><td class=\"n\">{c.Count.ToString(culture)}</td><td>{E(c.Robots)}</td><td>{E(c.LastSeen?.ToString("g", culture))}</td></tr>");
            }

            html.Append("</table>");

            html.Append("<table><tr><th>Robot</th><th class=\"n\">Alarms</th><th class=\"n\">Codes</th><th>Most frequent</th><th>Mean time between</th></tr>");
            foreach (RobotAlarmSummary r in data.RobotAlarms)
            {
                html.Append(CultureInfo.InvariantCulture,
                    $"<tr><td>{E(r.Robot)}</td><td class=\"n\">{r.Count.ToString(culture)}</td><td class=\"n\">{r.DistinctCodes.ToString(culture)}</td><td><code>{E(r.TopCode)}</code></td><td>{E(r.MeanTimeBetweenText)}</td></tr>");
            }

            html.Append("</table>");
        }

        html.Append("<h2>Watched setting changes</h2>");
        if (data.SettingChanges.Count == 0)
        {
            html.Append("<p class=\"ok\">No changes to frames, payload, mastering, reference positions, joint limits, DCS or software version between backups in the period.</p>");
        }
        else
        {
            html.Append("<table><tr><th>Backup</th><th>Robot</th><th>What</th><th>Variable</th><th>Before</th><th>After</th></tr>");
            foreach (SettingChange s in data.SettingChanges)
            {
                html.Append(CultureInfo.InvariantCulture,
                    $"<tr><td>{E(s.NewerStamp)}</td><td>{E(s.Robot)}</td><td class=\"warn\">{E(s.Category)}</td><td><code>{E(s.Variable)}</code></td><td><code>{E(s.OldText)}</code></td><td><code>{E(s.NewText)}</code></td></tr>");
            }

            html.Append("</table>");
        }

        html.Append(CultureInfo.InvariantCulture,
            $"<p class=\"muted\">From the backups in <code>{E(data.ArchiveRoot)}</code>. Nothing was read from the robots to make this report.</p></body></html>");
        return html.ToString();
    }

    private static void Tile(StringBuilder html, string value, string label, string? css = null) =>
        html.Append(CultureInfo.InvariantCulture, $"<div class=\"tile\"><b class=\"{css}\">{E(value)}</b><span class=\"muted\">{E(label)}</span></div>");

    private static string When(DateTimeOffset? utc, CultureInfo culture) => utc?.ToLocalTime().ToString("g", culture) ?? "none";

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
