namespace RobControl.Core.Backup;

/// <summary>
/// The exclude patterns of a backup: <c>*</c> (any run of characters, not crossing a <c>/</c>) and
/// <c>?</c> (one character), case-insensitive, nothing else. A pattern with a <c>/</c> is matched against
/// the name inside the device (<c>LOGBOOK/*.LS</c>); one without is matched against the bare file name
/// (<c>*.DG</c>) wherever it is. Deliberately small: no regex, nothing a typo can turn into "everything".
/// </summary>
public static class FileNamePattern
{
    /// <summary>The first of <paramref name="patterns"/> that matches, or null.</summary>
    public static string? FirstMatch(IEnumerable<string> patterns, string pathInDevice)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(pathInDevice);
        string path = pathInDevice.Replace('\\', '/');
        int slash = path.LastIndexOf('/');
        string name = slash < 0 ? path : path[(slash + 1)..];

        foreach (string raw in patterns)
        {
            string pattern = (raw ?? string.Empty).Trim().Replace('\\', '/');
            if (pattern.Length == 0)
            {
                continue;
            }

            if (IsMatch(pattern, pattern.Contains('/', StringComparison.Ordinal) ? path : name))
            {
                return pattern;
            }
        }

        return null;
    }

    public static bool IsMatch(string pattern, string text)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(text);
        return Match(pattern.AsSpan(), text.AsSpan());
    }

    private static bool Match(ReadOnlySpan<char> p, ReadOnlySpan<char> t)
    {
        // Iterative with one backtrack point - linear enough for names of 128 characters.
        int pi = 0, ti = 0, star = -1, mark = 0;
        while (ti < t.Length)
        {
            if (pi < p.Length && (p[pi] == '?' ? t[ti] != '/' : Same(p[pi], t[ti])) && p[pi] != '*')
            {
                pi++;
                ti++;
            }
            else if (pi < p.Length && p[pi] == '*')
            {
                star = pi++;
                mark = ti;
            }
            else if (star >= 0 && t[mark] != '/')
            {
                pi = star + 1;
                ti = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (pi < p.Length && p[pi] == '*')
        {
            pi++;
        }

        return pi == p.Length;
    }

    private static bool Same(char a, char b) => char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
}
