namespace RobControl.Core.Insight;

/// <summary>
/// One alarm from a controller's alarm log. <see cref="When"/> is the controller's own clock, as
/// written in the log - local to the plant, with no time zone - or null when the line had no date.
/// </summary>
public sealed record AlarmEntry(string Robot, DateTime? When, string Code, string Message, string Line)
{
    /// <summary>The facility part of the code - <c>SRVO</c>, <c>INTP</c>, <c>SPOT</c>.</summary>
    public string Facility => Code.Split('-')[0];

    public AlarmConcern Concern => AlarmConcerns.Classify(Code);
}
