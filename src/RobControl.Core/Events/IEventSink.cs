namespace RobControl.Core.Events;

/// <summary>
/// Where Core writes its record. The fleet database implements it; tests collect into a list.
///
/// <para>An interface so the engine does not know there is a database. <b>Implementations must not
/// throw</b> - a backup that fails because its log row could not be written has turned a record
/// problem into a robot problem. Swallow, and say so in the diagnostic log.</para>
/// </summary>
public interface IEventSink
{
    void Record(RobotEvent entry);
}
