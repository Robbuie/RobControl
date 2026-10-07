namespace RobControl.Core.Insight;

/// <summary>
/// Which alarm codes are called out. Codes from FANUC's alarm code list; the list is short on
/// purpose - a call-out that fires on everything is ignored.
/// </summary>
public static class AlarmConcerns
{
    private static readonly Dictionary<string, AlarmConcern> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SRVO-065"] = AlarmConcern.Battery,   // BLAL - battery voltage low
        ["SRVO-062"] = AlarmConcern.Battery,   // BZAL - battery zero; mastering data lost
        ["SRVO-050"] = AlarmConcern.Collision, // Collision detect
        ["SRVO-053"] = AlarmConcern.Collision, // Disturbance excess
        ["SRVO-038"] = AlarmConcern.Mastering, // Pulse mismatch
        ["SRVO-075"] = AlarmConcern.Mastering, // Pulse not established
    };

    public static AlarmConcern Classify(string code) =>
        Codes.TryGetValue(code ?? string.Empty, out AlarmConcern concern) ? concern : AlarmConcern.None;

    public static string Describe(AlarmConcern concern) => concern switch
    {
        AlarmConcern.Battery => "Battery",
        AlarmConcern.Collision => "Collision",
        AlarmConcern.Mastering => "Mastering",
        _ => string.Empty,
    };
}
