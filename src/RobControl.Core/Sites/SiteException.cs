namespace RobControl.Core.Sites;

/// <summary>A site could not be created, read, saved, exported or imported. The message says which and why.</summary>
public sealed class SiteException : RobControlException
{
    public SiteException(string message, Exception? inner = null) : base(message, inner) { }
}
