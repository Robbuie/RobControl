namespace RobControl.Core.Kcl;

/// <summary>A KCL command was refused by <see cref="KclClassifier"/>. Nothing was sent.</summary>
public sealed class KclRefusedException : RobControlException
{
    public KclRefusedException(KclClassification classification)
        : base($"Not sent: '{classification?.Normalised}'. {classification?.Reason}")
    {
        ArgumentNullException.ThrowIfNull(classification);
        Classification = classification;
        Remediation = classification.Class == KclCommandClass.Write
            ? "Writes will come with a gate that is armed per robot and read back. Until then, change it on the pendant."
            : "Do it on the pendant, where somebody can see the robot.";
    }

    public KclClassification Classification { get; }
}
