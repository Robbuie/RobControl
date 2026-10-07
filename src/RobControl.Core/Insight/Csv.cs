using System.Globalization;
using System.Text;

namespace RobControl.Core.Insight;

/// <summary>
/// CSV the way every export in RobControl writes it: comma-separated, quoted where needed, dates in
/// ISO form so it opens cleanly in Excel in any locale. Callers write it with a UTF-8 byte-order
/// mark, which is what Excel needs to read anything beyond ASCII correctly.
/// </summary>
public static class Csv
{
    /// <summary>A UTF-8 encoding that writes the byte-order mark - for <c>File.WriteAllText</c>.</summary>
    public static Encoding Encoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// A CSV field. A leading = + - @ is prefixed with a quote mark so text from a controller or a
    /// site file - a robot name, an alarm message, a program line - can never become a formula.
    /// </summary>
    public static string Field(string? value)
    {
        string text = value ?? string.Empty;
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0], StringComparison.Ordinal))
        {
            text = "'" + text;
        }

        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 || text.StartsWith('\'')
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }

    /// <summary>One line: every value through <see cref="Field"/>, joined with commas.</summary>
    public static string Row(params string?[] values) => string.Join(",", values.Select(Field));

    /// <summary>Local time, minutes - what a person reads in Excel.</summary>
    public static string Time(DateTimeOffset? utc) =>
        utc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Local time, seconds - for logs, where order within a minute matters.</summary>
    public static string TimeSeconds(DateTimeOffset? utc) =>
        utc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;

    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
