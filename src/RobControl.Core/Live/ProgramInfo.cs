namespace RobControl.Core.Live;

/// <summary>
/// One program from <c>PRGSTATE.DG</c>'s PROGRAM STATES: name and type on one line, then
/// <c>Key: value</c> lines - Comment, Program size, Lines, Created time, Last modified, Source file,
/// Protection, Task, Priority and more. Every field is kept in <see cref="Fields"/>; the common ones
/// are pulled out.
/// </summary>
public sealed record ProgramInfo(string Name, string Type, IReadOnlyList<KeyValuePair<string, string>> Fields)
{
    public string Comment => Field("Comment") ?? string.Empty;

    /// <summary>"Task: yes" - the program is a running task right now.</summary>
    public bool IsTask => Field("Task")?.StartsWith("yes", StringComparison.OrdinalIgnoreCase) == true;

    public int? SizeBytes => Number(Field("Program size") ?? Field("Size"));

    public int? Lines => Number(Field("Lines"));

    public string? Created => Field("Created time");

    public string? Modified => Field("Last modified");

    public DateTime? ModifiedTime => ProgramStateParser.ParseTime(Modified);

    public string? SourceFile => Field("Source file");

    public bool? WriteProtected => Field("Protection") switch
    {
        null => null,
        var p => p.StartsWith("ON", StringComparison.OrdinalIgnoreCase),
    };

    public string? Field(string key)
    {
        foreach (KeyValuePair<string, string> field in Fields)
        {
            if (field.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return field.Value;
            }
        }

        return null;
    }

    private static int? Number(string? text)
    {
        string? first = text?.Trim().Split(' ', 2)[0];
        return int.TryParse(first, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : null;
    }
}
