using System.Globalization;
using RobControl.Core.History;

namespace RobControl.App.ViewModels;

/// <summary>One line in the diff pane, or a hunk header between runs of lines.</summary>
public sealed class DiffLineViewModel
{
    private DiffLineViewModel(string kind, string oldNumber, string newNumber, string marker, string text)
    {
        Kind = kind;
        OldNumber = oldNumber;
        NewNumber = newNumber;
        Marker = marker;
        Text = text;
    }

    /// <summary>"same", "added", "removed" or "header" - what the row style keys its tint on.</summary>
    public string Kind { get; }

    public string OldNumber { get; }

    public string NewNumber { get; }

    public string Marker { get; }

    public string Text { get; }

    public static DiffLineViewModel From(DiffLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new DiffLineViewModel(
            line.Kind switch
            {
                DiffLineKind.Added => "added",
                DiffLineKind.Removed => "removed",
                _ => "same",
            },
            line.OldNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            line.NewNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            line.Kind switch
            {
                DiffLineKind.Added => "+",
                DiffLineKind.Removed => "-",
                _ => " ",
            },
            line.Text);
    }

    public static DiffLineViewModel Header(DiffHunk hunk)
    {
        ArgumentNullException.ThrowIfNull(hunk);
        return new DiffLineViewModel("header", string.Empty, string.Empty, string.Empty, hunk.Header);
    }

    public static DiffLineViewModel Note(string text) =>
        new("header", string.Empty, string.Empty, string.Empty, text);
}
