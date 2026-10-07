using System.Text.RegularExpressions;

namespace RobControl.Core.Insight;

/// <summary>
/// Reads a TP program listing (<c>.LS</c>): its name, comment, numbered lines and what each line
/// refers to.
///
/// <para>Like every parser here it <b>searches rather than parses</b>: it looks for <c>/PROG</c>, the
/// <c>COMMENT</c> attribute and the numbered <c>/MN</c> lines, and for references by pattern. A line
/// it cannot make sense of is still kept as text, so search sees it even when cross-reference does
/// not. The format is from FANUC listings; Phase 0 captures will show any variation per version.</para>
/// </summary>
public static partial class ProgramListingParser
{
    /// <summary>The listing in <paramref name="lines"/>, or null when it is not a TP program (a .LS that is not one, a KAREL listing).</summary>
    public static ProgramListing? Parse(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        string? name = null;
        string? comment = null;
        bool inMain = false;
        var body = new List<ProgramLine>();
        var references = new List<ProgramReference>();

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd();
            if (name is null)
            {
                Match prog = ProgHeader().Match(line);
                if (prog.Success)
                {
                    name = prog.Groups[1].Value.ToUpperInvariant();
                }

                continue;
            }

            if (line.StartsWith('/'))
            {
                inMain = line.StartsWith("/MN", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inMain)
            {
                Match c = CommentAttribute().Match(line);
                if (comment is null && c.Success)
                {
                    comment = c.Groups[1].Value.Trim();
                }

                continue;
            }

            Match numbered = NumberedLine().Match(line);
            if (!numbered.Success || !int.TryParse(numbered.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number))
            {
                continue;
            }

            string text = numbered.Groups[2].Value.Trim();
            if (text.EndsWith(';'))
            {
                text = text[..^1].TrimEnd();
            }

            body.Add(new ProgramLine(number, text));
            references.AddRange(ReferencesIn(text, number));
        }

        return name is null ? null : new ProgramListing(name, string.IsNullOrEmpty(comment) ? null : comment, body, references);
    }

    /// <summary>Every reference on one line. Text inside a comment line (<c>!...</c>) is not a reference.</summary>
    public static IEnumerable<ProgramReference> ReferencesIn(string text, int line)
    {
        ArgumentNullException.ThrowIfNull(text);
        string code = text.TrimStart();
        if (code.StartsWith('!') || code.StartsWith("//", StringComparison.Ordinal))
        {
            yield break;
        }

        foreach (Match call in CallOrRun().Matches(code))
        {
            yield return new ProgramReference(ReferenceKind.Program, call.Groups[2].Value.ToUpperInvariant(), line);
        }

        foreach (Match port in PortReference().Matches(code))
        {
            string kind = port.Groups[1].Value.ToUpperInvariant();
            int index = int.Parse(port.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            ReferenceKind referenceKind = kind switch
            {
                "R" => ReferenceKind.Register,
                "PR" => ReferenceKind.PositionRegister,
                "SR" => ReferenceKind.StringRegister,
                _ => ReferenceKind.Io,
            };
            yield return new ProgramReference(referenceKind, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{kind}[{index}]"), line);
        }
    }

    /// <summary>
    /// A pattern that finds <paramref name="target"/> as written on a pendant line: <c>R[45]</c> also
    /// matches <c>R[45:Weld count]</c> and <c>R[ 45]</c>; <c>PR[3]</c> matches <c>PR[3,2]</c>. Null when
    /// the text is not a register or port reference.
    /// </summary>
    public static Regex? ReferencePattern(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Match m = BareReference().Match(target.Trim());
        if (!m.Success)
        {
            return null;
        }

        string kind = Regex.Escape(m.Groups[1].Value);
        string index = m.Groups[2].Value.TrimStart('0');
        index = index.Length == 0 ? "0" : index;
        return new Regex(
            $@"(?<![A-Z0-9_$]){kind}\[\s*0*{index}\s*(?:,\s*\d+\s*)?(?::[^\]]*)?\]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));
    }

    [GeneratedRegex(@"^\s*/PROG\s+([A-Za-z0-9_]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ProgHeader();

    [GeneratedRegex(@"^\s*COMMENT\s*=\s*""(.*)""\s*;?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommentAttribute();

    [GeneratedRegex(@"^\s*(\d+):(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedLine();

    [GeneratedRegex(@"(?<![A-Z0-9_])(CALL|RUN)\s+([A-Z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CallOrRun();

    // The kinds a pendant line can name by index. The look-behind keeps UFRAME[1] from reading as F[1]
    // and SDO[1] as DO[1]; AR (arguments), P (positions) and TIMER are left out on purpose.
    [GeneratedRegex(@"(?<![A-Z0-9_$])(PR|SR|R|DI|DO|RI|RO|GI|GO|AI|AO|UI|UO|SI|SO|WI|WO|F|M)\[\s*(\d+)\s*(?:,\s*\d+\s*)?(?::[^\]]*)?\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PortReference();

    [GeneratedRegex(@"^(PR|SR|R|DI|DO|RI|RO|GI|GO|AI|AO|UI|UO|SI|SO|WI|WO|F|M)\[\s*(\d+)\s*\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareReference();
}
