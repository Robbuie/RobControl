namespace RobControl.Core.Backup;

/// <summary>The archive folder, or something in it, is not usable.</summary>
public sealed class BackupArchiveException : RobControlException
{
    public BackupArchiveException(string message, Exception? inner = null) : base(message, inner) { }
}
