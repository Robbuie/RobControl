namespace RobControl.Core.Insight;

/// <summary>One alarm code across the fleet: how often, on how many robots, first and last seen.</summary>
public sealed record AlarmCodeSummary(string Code, string Message, int Count, int RobotCount, string Robots, DateTime? FirstSeen, DateTime? LastSeen, AlarmConcern Concern)
{
    public string ConcernText => AlarmConcerns.Describe(Concern);
}
