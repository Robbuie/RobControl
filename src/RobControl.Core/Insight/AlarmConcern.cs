namespace RobControl.Core.Insight;

/// <summary>Alarms worth calling out on their own, because each means a job to plan, not just a reset.</summary>
public enum AlarmConcern
{
    None,

    /// <summary>Pulse-coder battery low or gone - mastering is at risk.</summary>
    Battery,

    /// <summary>A collision or disturbance-torque trip.</summary>
    Collision,

    /// <summary>Mastering lost or pulse mismatch - the robot needs remastering or checking.</summary>
    Mastering,
}
