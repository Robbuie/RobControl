using RobControl.Core.Systems;

namespace RobControl.App.ViewModels;

/// <summary>
/// What the system dialog edits: name, notes and the system's backup settings, as text. It becomes
/// <see cref="SystemSettings"/> only through <see cref="TryBuild"/>, which keeps the robot list.
/// </summary>
public sealed record SystemDraft
{
    public string Name { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public BackupProfileDraft Backup { get; init; } = new();

    /// <summary>Where the file is, to show in the dialog.</summary>
    public string SettingsPath { get; init; } = string.Empty;

    public static SystemDraft From(RobotSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        return new SystemDraft
        {
            Name = system.Name,
            Notes = system.Settings.Notes ?? string.Empty,
            Backup = BackupProfileDraft.From(system.Settings.Backup),
            SettingsPath = system.SettingsPath,
        };
    }

    public bool TryBuild(SystemSettings current, out SystemSettings? settings, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(current);
        settings = null;
        if (!SystemCatalog.IsValidName(Name, out problem))
        {
            return false;
        }

        if (!Backup.TryBuild(out BackupProfile? backup, out problem))
        {
            return false;
        }

        settings = current with
        {
            Name = Name.Trim(),
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            Backup = backup ?? new BackupProfile(),
        };
        return true;
    }
}
