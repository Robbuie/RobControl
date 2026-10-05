namespace RobControl.Core.Persistence;

/// <summary>The fleet database could not be opened, read or written.</summary>
public sealed class PersistenceException : RobControlException
{
    public PersistenceException(string message, Exception? inner = null) : base(message, inner) { }
}
