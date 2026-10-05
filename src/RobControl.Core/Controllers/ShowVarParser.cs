namespace RobControl.Core.Controllers;

/// <summary>
/// The value out of a <c>SHOW VAR</c> reply. Tolerant by design, for the same reason as
/// <see cref="ControllerIdentityParser"/>: the exact layout is unconfirmed (Phase 0), but every
/// variant reported puts the value after the last <c>=</c> on the line naming the variable.
/// </summary>
public static class ShowVarParser
{
    /// <summary>The value as text with surrounding quotes removed, or null if there is no value.</summary>
    public static string? Value(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.Trim();
            int equals = line.LastIndexOf('=');
            if (equals < 0)
            {
                continue;
            }

            string value = line[(equals + 1)..].Trim();
            if (value.Length >= 2 && value[0] is '\'' or '"' && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            return value;
        }

        return null;
    }
}
