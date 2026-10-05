namespace RobControl.Core;

/// <summary>
/// Base for every exception this engine raises deliberately.
///
/// <para>The rule that matters is in the message, not the type: a failure the user can see must
/// name the likely cause and the next action. "FTP failed" is useless; "R2-14 (10.20.1.54) refused
/// the FTP login - the controller may require a password: Setup &gt; Host Comm &gt; FTP" is the
/// product.</para>
/// </summary>
public abstract class RobControlException : Exception
{
    protected RobControlException(string message) : base(message) { }

    protected RobControlException(string message, Exception? inner) : base(message, inner) { }

    /// <summary>
    /// What the user should do next, in one sentence. The UI shows this beneath the message.
    /// Null only when there genuinely is no useful next step.
    /// </summary>
    public string? Remediation { get; init; }
}
