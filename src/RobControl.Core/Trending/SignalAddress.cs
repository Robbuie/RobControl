using System.Globalization;
using System.Text.RegularExpressions;
using RobControl.Core.Kcl;

namespace RobControl.Core.Trending;

/// <summary>
/// One thing to watch on a robot, written the way it is written on the pendant: <c>R[12]</c>,
/// <c>DI[3]</c>, <c>GO[1]</c>, <c>$SCR.$NUM_GROUP</c>.
///
/// <para><b>Reading only, by construction.</b> A system variable is only accepted if
/// <c>SHOW VAR &lt;it&gt;</c> classifies as a read, so nothing typed here can smuggle a second
/// command or reach a write. I/O is read from the diagnostic file - there is no path from a signal
/// to setting a port.</para>
/// </summary>
public sealed partial record SignalAddress
{
    /// <summary>The I/O types a FANUC controller reports. Position registers (PR) are not scalars and are not here.</summary>
    public static IReadOnlySet<string> IoTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "DI", "DO", "RI", "RO", "UI", "UO", "SI", "SO", "WI", "WO", "GI", "GO", "AI", "AO", "F", "M",
    };

    private SignalAddress(SignalKind kind, string text, string? ioType, int? index)
    {
        Kind = kind;
        Text = text;
        IoType = ioType;
        Index = index;
    }

    public SignalKind Kind { get; }

    /// <summary>Normalised: <c>R[12]</c>, <c>DI[3]</c>, <c>$SCR.$NUM_GROUP</c>. Also the storage key.</summary>
    public string Text { get; }

    /// <summary>"DI", "GO"... for I/O; null otherwise.</summary>
    public string? IoType { get; }

    /// <summary>The number in brackets, for registers and I/O.</summary>
    public int? Index { get; }

    /// <summary>On/off signals - plotted as steps between 0 and 1.</summary>
    public bool IsDigital => Kind == SignalKind.Io && IoType is not ("GI" or "GO" or "AI" or "AO");

    public override string ToString() => Text;

    public static SignalAddress Parse(string text) =>
        TryParse(text, out SignalAddress? address, out string? problem) ? address! : throw new FormatException(problem);

    public static bool TryParse(string? text, out SignalAddress? address, out string? problem)
    {
        address = null;
        problem = null;
        string t = (text ?? string.Empty).Trim();

        if (t.Length == 0)
        {
            problem = "Empty signal.";
            return false;
        }

        Match bracket = Bracketed().Match(t);
        if (bracket.Success)
        {
            string type = bracket.Groups["type"].Value.ToUpperInvariant();
            if (!int.TryParse(bracket.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1 || n > 100000)
            {
                problem = $"'{t}': the number in brackets is out of range.";
                return false;
            }

            if (type is "R" or "NUMREG")
            {
                address = new SignalAddress(SignalKind.NumericRegister, $"R[{n}]", null, n);
                return true;
            }

            if (IoTypes.Contains(type))
            {
                address = new SignalAddress(SignalKind.Io, $"{type}[{n}]", type, n);
                return true;
            }

            if (type is "PR" or "SR" or "AR")
            {
                problem = $"'{t}': {type} registers are not single numbers and cannot be trended yet.";
                return false;
            }
        }

        if (t[0] is '$' or '[')
        {
            KclClassification check = KclClassifier.Classify("SHOW VAR " + t);
            if (t.Contains(' ', StringComparison.Ordinal) || check.Class != KclCommandClass.Read)
            {
                problem = $"'{t}' is not a single variable name.";
                return false;
            }

            address = new SignalAddress(SignalKind.SystemVariable, t.ToUpperInvariant(), null, null);
            return true;
        }

        problem = $"'{t}' is not a signal. Write it as on the pendant: R[5], DI[3], GO[1], or a $system.variable.";
        return false;
    }

    /// <summary>
    /// A list as typed: separated by commas, semicolons or spaces, with ranges for registers and
    /// I/O - <c>R[1-10], DI[1..8], $SCR.$NUM_GROUP</c>. All or nothing: one bad entry and the
    /// problems are returned with no addresses, so a typo is fixed rather than half-applied.
    /// </summary>
    public static IReadOnlyList<SignalAddress> ParseList(string? text, out IReadOnlyList<string> problems, int maxCount = 500)
    {
        var result = new List<SignalAddress>();
        var errors = new List<string>();

        foreach (string raw in (text ?? string.Empty).Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            Match range = Range().Match(raw);
            if (range.Success)
            {
                int from = int.Parse(range.Groups["a"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                int to = int.Parse(range.Groups["b"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                if (to < from || to - from >= maxCount)
                {
                    errors.Add($"'{raw}': the range is backwards or too long.");
                    continue;
                }

                for (int i = from; i <= to; i++)
                {
                    Add($"{range.Groups["type"].Value}[{i}]");
                }

                continue;
            }

            Add(raw);
        }

        if (result.Count > maxCount)
        {
            errors.Add($"{result.Count} signals is more than {maxCount} at once.");
        }

        problems = errors;
        return errors.Count > 0 ? [] : [.. result.DistinctBy(a => a.Text)];

        void Add(string one)
        {
            if (TryParse(one, out SignalAddress? a, out string? p))
            {
                result.Add(a!);
            }
            else
            {
                errors.Add(p!);
            }
        }
    }

    [GeneratedRegex(@"^(?<type>[A-Za-z]{1,6})\[\s*(?<n>\d{1,6})\s*\]$")]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"^(?<type>[A-Za-z]{1,6})\[\s*(?<a>\d{1,6})\s*(?:-|\.\.)\s*(?<b>\d{1,6})\s*\]$")]
    private static partial Regex Range();
}
