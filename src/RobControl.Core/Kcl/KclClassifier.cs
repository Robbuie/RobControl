namespace RobControl.Core.Kcl;

/// <summary>
/// Decides, before anything is sent, whether a KCL command is a read, a write, or never allowed.
///
/// <para><b>The rule is an allow-list, not a deny-list.</b> KCL accepts abbreviations and has
/// dozens of commands across controller versions; a list of dangerous commands would miss one. So
/// reads are recognised by name, <c>SET VAR</c> is recognised as the one write, and anything else -
/// including anything misspelled, abbreviated in a way not listed, or simply unknown - is
/// <see cref="KclCommandClass.Never"/>. The free-text console goes through this same method.</para>
///
/// <para>This is the line in CLAUDE.md's Safety section, in code. Widening it is a change to that
/// section first.</para>
/// </summary>
public static class KclClassifier
{
    // The verbs that only read. Abbreviations are listed explicitly rather than matched by prefix:
    // a prefix rule would let "S" through, and on some versions S abbreviates something else.
    private static readonly Dictionary<string, string> ReadVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SHOW"] = "SHOW",
        ["SH"] = "SHOW",
        ["DIRECTORY"] = "DIRECTORY",
        ["DIR"] = "DIRECTORY",
        ["TYPE"] = "TYPE",
        ["HELP"] = "HELP",
    };

    // System variables that a SET VAR may never touch, gate or no gate: dual check safety, safety
    // I/O configuration, and the user/password tables. A prefix match on the name after the $.
    private static readonly string[] ProtectedVariablePrefixes =
    [
        "$DCSS", "$DCS_", "$DCS ", "$SAFE", "$SFT", "$PASSWORD", "$PSWD", "$MASTER", "$DMR_GRP",
    ];

    /// <summary>Classifies one command. Never throws for odd input: odd input is <see cref="KclCommandClass.Never"/>.</summary>
    public static KclClassification Classify(string? command)
    {
        string text = (command ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return Never(text, string.Empty, "Empty command.");
        }

        if (text.AsSpan().IndexOfAny("\r\n\t;|<>`") >= 0 || text.Any(char.IsControl))
        {
            return Never(text, string.Empty, "The command contains a line break, a separator or a redirection.");
        }

        // Collapse runs of spaces so "SHOW   VAR" and "SHOW VAR" are the same command on the wire.
        string normalised = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        string[] words = normalised.Split(' ');
        string first = words[0];

        if (ReadVerbs.TryGetValue(first, out string? verb))
        {
            return new KclClassification(KclCommandClass.Read, normalised, verb, $"{verb} only reads.");
        }

        if (first.Equals("SET", StringComparison.OrdinalIgnoreCase)
            && words.Length >= 3
            && words[1].Equals("VAR", StringComparison.OrdinalIgnoreCase))
        {
            string target = words[2];

            // Strip a program prefix - [*SYSTEM*]$DCSS_... or [PROG]VAR - and only a prefix. A ']'
            // further in belongs to an array index ($DCSS_CPC[1].$ENABLE), and cutting there would
            // leave ".$ENABLE" and wave a safety variable through.
            int bracket = target.StartsWith('[') ? target.IndexOf(']', StringComparison.Ordinal) : -1;
            string variable = bracket >= 0 ? target[(bracket + 1)..] : target;

            foreach (string prefix in ProtectedVariablePrefixes)
            {
                if (variable.StartsWith(prefix.TrimEnd(), StringComparison.OrdinalIgnoreCase))
                {
                    return Never(normalised, "SET VAR",
                        $"{variable} is safety, mastering or security configuration. RobControl never writes it.");
                }
            }

            return new KclClassification(KclCommandClass.Write, normalised, "SET VAR",
                "SET VAR changes a value on the controller. Writes are not enabled in this version of RobControl.");
        }

        return Never(normalised, first.ToUpperInvariant(),
            $"{first.ToUpperInvariant()} is not a read. RobControl only sends SHOW, DIRECTORY, TYPE and HELP.");
    }

    private static KclClassification Never(string normalised, string verb, string reason) =>
        new(KclCommandClass.Never, normalised, verb, reason);
}
