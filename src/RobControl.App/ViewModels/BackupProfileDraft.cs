using System.Globalization;
using RobControl.Core.Systems;

namespace RobControl.App.ViewModels;

/// <summary>
/// The backup settings a dialog edits - a system's defaults, or one robot's overrides - as text.
/// <b>Blank means "not set here"</b>: for a robot, use the system's; for a system, use the built-in
/// default. It becomes a <see cref="BackupProfile"/> only through <see cref="TryBuild"/>.
/// </summary>
public sealed record BackupProfileDraft
{
    /// <summary>"MD:, FR:" - comma, space or semicolon separated.</summary>
    public string Devices { get; init; } = string.Empty;

    /// <summary>One pattern per line, or comma separated: <c>*.DG</c>.</summary>
    public string Exclude { get; init; } = string.Empty;

    /// <summary>Null: not set here.</summary>
    public bool? IncludeSubfolders { get; init; }

    public string PauseMs { get; init; } = string.Empty;

    public string Attempts { get; init; } = string.Empty;

    public static BackupProfileDraft From(BackupProfile? profile) => profile is null ? new BackupProfileDraft() : new BackupProfileDraft
    {
        Devices = profile.Devices is null ? string.Empty : string.Join(", ", profile.Devices),
        Exclude = profile.Exclude is null ? string.Empty : string.Join(Environment.NewLine, profile.Exclude),
        IncludeSubfolders = profile.IncludeSubfolders,
        PauseMs = profile.PauseBetweenFilesMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        Attempts = profile.AttemptsPerFile?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
    };

    public bool TryBuild(out BackupProfile? profile, out string? problem)
    {
        profile = null;
        problem = null;

        IReadOnlyList<string>? devices = null;
        string[] typed = Split(Devices);
        if (typed.Length > 0)
        {
            string? bad = typed.FirstOrDefault(d => !BackupProfile.TryNormaliseDevice(d, out _));
            if (bad is not null)
            {
                problem = $"'{bad}' is not a controller device. Devices look like MD:, FR:, FRA:, MC:, UD1:.";
                return false;
            }

            devices = BackupProfile.NormaliseDevices(typed);
        }

        string[] patterns = Split(Exclude, keepSpaces: true);
        IReadOnlyList<string>? exclude = patterns.Length > 0 ? BackupProfile.NormaliseExcludes(patterns) : null;

        int? pause = null;
        if (PauseMs.Trim().Length > 0)
        {
            if (!int.TryParse(PauseMs.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int p) || p > 10_000)
            {
                problem = "Pause between files is a whole number of milliseconds from 0 to 10000.";
                return false;
            }

            pause = p;
        }

        int? attempts = null;
        if (Attempts.Trim().Length > 0)
        {
            if (!int.TryParse(Attempts.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int a) || a is < 1 or > 5)
            {
                problem = "Tries per file is a whole number from 1 to 5.";
                return false;
            }

            attempts = a;
        }

        profile = new BackupProfile
        {
            Devices = devices,
            Exclude = exclude,
            IncludeSubfolders = IncludeSubfolders,
            PauseBetweenFilesMs = pause,
            AttemptsPerFile = attempts,
        };
        return true;
    }

    private static string[] Split(string text, bool keepSpaces = false) =>
        (text ?? string.Empty).Split(keepSpaces ? ['\r', '\n', ',', ';'] : ['\r', '\n', ',', ';', ' ', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
