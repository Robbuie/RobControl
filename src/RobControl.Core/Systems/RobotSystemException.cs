namespace RobControl.Core.Systems;

/// <summary>A system could not be created, read or saved. The message says which and why.</summary>
public sealed class RobotSystemException : RobControlException
{
    public RobotSystemException(string message, Exception? inner = null) : base(message, inner) { }
}
