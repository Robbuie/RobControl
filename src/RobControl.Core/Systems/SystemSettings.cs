using System.Text.Json.Serialization;
using RobControl.Core.Robots;

namespace RobControl.Core.Systems;

/// <summary>
/// One system - a line, a cell, a group of robots that are worked on together - as written in
/// <c>Documents\RobControl\Systems\&lt;site&gt;\&lt;system&gt;\system.json</c>.
///
/// <para>Plain JSON, camelCase, comments and trailing commas forgiven, every value checked on load:
/// the same contract as <c>site.json</c>. Hand-editable while the system is not open; the app rewrites
/// it when something in it changes.</para>
/// </summary>
public sealed record SystemSettings
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; init; }

    /// <summary>How this system's robots are backed up unless a robot says otherwise.</summary>
    [JsonPropertyName("backup")]
    public BackupProfile Backup { get; init; } = new();

    /// <summary>The robots in this system, by address.</summary>
    [JsonPropertyName("robots")]
    public IReadOnlyList<SystemRobot> Robots { get; init; } = [];

    /// <summary>This system's entry for <paramref name="robot"/>, or null when the robot is not in it.</summary>
    public SystemRobot? EntryFor(Robot robot) => Robots.FirstOrDefault(r => r.Matches(robot));

    public bool Contains(Robot robot) => EntryFor(robot) is not null;

    /// <summary>The same settings with names trimmed and robot entries de-duplicated (first wins).</summary>
    public SystemSettings Normalised() => this with
    {
        Name = (Name ?? string.Empty).Trim(),
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
        Backup = Backup ?? new BackupProfile(),
        Robots = [.. (Robots ?? [])
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Address))
            .Select(r => r with { Address = r.Address.Trim(), FtpPort = r.FtpPort is > 0 and < 65536 ? r.FtpPort : 21 })
            .DistinctBy(r => (r.Address.ToUpperInvariant(), r.FtpPort))],
    };

    /// <summary>With <paramref name="robot"/> added, or its entry refreshed (name; backup if given).</summary>
    public SystemSettings With(Robot robot, BackupProfile? backup = null, bool replaceBackup = false)
    {
        ArgumentNullException.ThrowIfNull(robot);
        SystemRobot? existing = EntryFor(robot);
        BackupProfile? keep = replaceBackup ? backup : backup ?? existing?.Backup;
        SystemRobot entry = SystemRobot.From(robot, keep);
        return this with
        {
            Robots = existing is null
                ? [.. Robots, entry]
                : [.. Robots.Select(r => ReferenceEquals(r, existing) ? entry : r)],
        };
    }

    /// <summary>Without <paramref name="robot"/>'s entry.</summary>
    public SystemSettings Without(Robot robot)
    {
        ArgumentNullException.ThrowIfNull(robot);
        return this with { Robots = [.. Robots.Where(r => !r.Matches(robot))] };
    }

    /// <summary>The entry that was <paramref name="before"/>, moved to <paramref name="after"/>'s address and name.</summary>
    public SystemSettings Renamed(Robot before, Robot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        SystemRobot? existing = EntryFor(before);
        if (existing is null)
        {
            return this;
        }

        SystemRobot entry = SystemRobot.From(after, existing.Backup);
        return this with { Robots = [.. Robots.Where(r => !r.Matches(after) || ReferenceEquals(r, existing)).Select(r => ReferenceEquals(r, existing) ? entry : r)] };
    }
}
