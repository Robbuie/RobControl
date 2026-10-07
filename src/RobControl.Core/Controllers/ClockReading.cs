using System.Globalization;

namespace RobControl.Core.Controllers;

/// <summary>
/// How far a controller's clock is from this PC's, read from the <c>Date</c> header its web server
/// sends with a reply.
///
/// <para>Why it matters: alarm logs, backup history and trends are lined up by time. A controller
/// whose clock has drifted ten minutes - or was never set after a battery change - puts its alarms in
/// the wrong place on every timeline RobControl draws.</para>
///
/// <para><b>Assumed, not confirmed:</b> a FANUC controller keeps local wall-clock time with no idea of
/// time zones, so its web server may well send local time labelled GMT. Both readings are tried and
/// the closer one is taken: a controller exactly some whole hours "off" in UTC but right in local time
/// is a correct clock with a mislabelled header, not a clock that is hours wrong.</para>
/// </summary>
/// <param name="Skew">Controller minus PC. Positive: the controller is ahead.</param>
/// <param name="AsLocalTime">The header read as local wall-clock time matched better than as UTC.</param>
public sealed record ClockReading(TimeSpan Skew, bool AsLocalTime)
{
    /// <summary>Closer than this is "right": the header has one-second resolution, and a probe takes a moment.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(2);

    public bool IsOff => Skew.Duration() > Tolerance;

    /// <summary>"Controller clock matches this PC (within 4 s)." / "Controller clock is 12 min 30 s behind this PC."</summary>
    public string Describe()
    {
        string amount = Amount(Skew.Duration());
        string basis = AsLocalTime ? " (its web server labels local time as GMT)" : string.Empty;
        return IsOff
            ? $"Controller clock is {amount} {(Skew > TimeSpan.Zero ? "ahead of" : "behind")} this PC{basis}."
            : $"Controller clock matches this PC (within {amount}){basis}.";
    }

    /// <summary>
    /// Compares a server's <c>Date</c> header with this PC's clock at the moment the reply arrived.
    /// </summary>
    public static ClockReading Compare(DateTimeOffset serverDate, DateTimeOffset pcUtc, TimeZoneInfo? localZone = null)
    {
        TimeZoneInfo zone = localZone ?? TimeZoneInfo.Local;

        // As sent: the header says GMT, and the controller meant it.
        TimeSpan asUtc = serverDate.UtcDateTime - pcUtc.UtcDateTime;

        // As local: the header's digits are the controller's wall clock; compare with ours.
        DateTime pcLocal = TimeZoneInfo.ConvertTimeFromUtc(pcUtc.UtcDateTime, zone);
        TimeSpan asLocal = DateTime.SpecifyKind(serverDate.UtcDateTime, DateTimeKind.Unspecified) - pcLocal;

        // Ties go to UTC: when this PC is on UTC both readings are the same thing.
        return asLocal.Duration() < asUtc.Duration()
            ? new ClockReading(asLocal, AsLocalTime: true)
            : new ClockReading(asUtc, AsLocalTime: false);
    }

    private static string Amount(TimeSpan span) => span switch
    {
        { TotalSeconds: < 60 } => string.Create(CultureInfo.InvariantCulture, $"{span.TotalSeconds:0} s"),
        { TotalHours: < 1 } => string.Create(CultureInfo.InvariantCulture, $"{span.Minutes} min {span.Seconds} s"),
        { TotalDays: < 1 } => string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours} h {span.Minutes} min"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays} d {span.Hours} h"),
    };
}
