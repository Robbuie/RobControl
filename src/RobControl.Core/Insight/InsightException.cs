namespace RobControl.Core.Insight;

/// <summary>A search, report or history view could not be produced. The message says why.</summary>
public sealed class InsightException : RobControlException
{
    public InsightException(string message, Exception? inner = null) : base(message, inner) { }
}
