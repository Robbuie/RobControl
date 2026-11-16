using System.Globalization;

namespace RobControl.Core.Trending;

/// <summary>One change of a watched signal: from what, to what, when, and how long it had held before.</summary>
public sealed record SignalTransition(string Signal, string Comment, DateTimeOffset Utc, double From, double To, TimeSpan? HeldBefore, bool IsDigital)
{
    public string When => Utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    public string Change => IsDigital
        ? $"{(From != 0 ? "ON" : "OFF")} -> {(To != 0 ? "ON" : "OFF")}"
        : string.Create(CultureInfo.CurrentCulture, $"{From:0.###} -> {To:0.###}");

    public string HeldText => HeldBefore is { } held ? SignalTransitions.Span(held) : "-";
}

/// <summary>Where a watched signal is now and for how long - and whether that is longer than it should be.</summary>
public sealed record SignalHold(string Signal, string Comment, double Value, bool IsDigital, DateTimeOffset Since, TimeSpan HeldFor, int Changes, bool Stuck)
{
    public string State => IsDigital ? (Value != 0 ? "ON" : "OFF") : Value.ToString("0.###", CultureInfo.CurrentCulture);

    public string HeldText => SignalTransitions.Span(HeldFor) + (Stuck ? " - longer than usual" : string.Empty);
}

/// <summary>
/// Turns a recorded signal into what a handshake question needs: every change with how long the state
/// before it held, and the state now with how long it has held.
///
/// <para><b>Stuck</b> means: held now for longer than <c>stuckFactor</c> times the longest the same state
/// held before, in the period shown (and at least a minute) - "DI[34] has been OFF for 4 min; before,
/// OFF never lasted more than 20 s". A signal that has never changed is not called stuck.</para>
///
/// <para><b>The recording's interval is the resolution.</b> At the 2 s minimum, a pulse shorter than
/// the interval can be missed altogether, and every time is good to one interval. This is for signals
/// that stick, not for timing a handshake to the millisecond.</para>
/// </summary>
public static class SignalTransitions
{
    public const double DefaultStuckFactor = 3;

    public static readonly TimeSpan MinimumStuck = TimeSpan.FromMinutes(1);

    public static IReadOnlyList<SignalTransition> Changes(string signal, string comment, bool digital, IReadOnlyList<TrendPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var changes = new List<SignalTransition>();
        DateTimeOffset? since = null;
        double? last = null;
        foreach (TrendPoint p in points.OrderBy(p => p.Utc))
        {
            if (last is null)
            {
                last = p.Value;
                since = p.Utc;
                continue;
            }

            if (!Same(last.Value, p.Value, digital))
            {
                changes.Add(new SignalTransition(signal, comment, p.Utc, last.Value, p.Value, p.Utc - since!.Value, digital));
                last = p.Value;
                since = p.Utc;
            }
        }

        // The first change's "held before" is only from the start of the period: not a full hold.
        if (changes.Count > 0)
        {
            changes[0] = changes[0] with { HeldBefore = null };
        }

        return changes;
    }

    public static SignalHold? Now(string signal, string comment, bool digital, IReadOnlyList<TrendPoint> points, DateTimeOffset now, double stuckFactor = DefaultStuckFactor)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            return null;
        }

        IReadOnlyList<SignalTransition> changes = Changes(signal, comment, digital, points);
        TrendPoint latest = points.MaxBy(p => p.Utc);
        DateTimeOffset since = changes.Count > 0 ? changes[^1].Utc : points.MinBy(p => p.Utc).Utc;
        TimeSpan held = now - since;

        // The longest the current state lasted before, from changes that left it.
        TimeSpan? longest = changes.Where(c => c.HeldBefore is not null && Same(c.From, latest.Value, digital))
            .Select(c => c.HeldBefore!.Value).DefaultIfEmpty().Max();
        bool stuck = changes.Count > 0 && longest is { } l && l > TimeSpan.Zero
            && held > MinimumStuck && held.TotalSeconds > l.TotalSeconds * stuckFactor;
        return new SignalHold(signal, comment, latest.Value, digital, since, held, changes.Count, stuck);
    }

    /// <summary>"4 s", "3 min 20 s", "2 h 05 min".</summary>
    public static string Span(TimeSpan span) => span switch
    {
        { TotalSeconds: < 60 } => string.Create(CultureInfo.CurrentCulture, $"{span.TotalSeconds:0} s"),
        { TotalHours: < 1 } => string.Create(CultureInfo.CurrentCulture, $"{span.Minutes} min {span.Seconds:00} s"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{(int)span.TotalHours} h {span.Minutes:00} min"),
    };

    private static bool Same(double a, double b, bool digital) => digital ? (a != 0) == (b != 0) : a.Equals(b);
}
