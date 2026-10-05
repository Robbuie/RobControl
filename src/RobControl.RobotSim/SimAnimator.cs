using System.Globalization;
using System.Text;

namespace RobControl.RobotSim;

/// <summary>
/// Makes the simulated controller's registers and I/O move, so trending can be watched with no
/// robot: a cycle counter, a slow sine, a noisy "current", a part-present input that toggles each
/// cycle and an output that follows it. Writes <c>NUMREG.VA</c> and <c>IOSTATE.DG</c> into
/// <see cref="SimController.MdOverrides"/> in the same synthetic layout as the fixture profile.
/// </summary>
public sealed class SimAnimator : IDisposable
{
    private readonly SimController _sim;
    private readonly Timer _timer;
    private readonly Random _random = new(42);
    private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;
    private int _cycles;

    public SimAnimator(SimController sim, TimeSpan? period = null)
    {
        _sim = sim ?? throw new ArgumentNullException(nameof(sim));
        Tick(null);
        TimeSpan every = period ?? TimeSpan.FromSeconds(1);
        _timer = new Timer(Tick, null, every, every);
    }

    public void Dispose() => _timer.Dispose();

    private void Tick(object? state)
    {
        double t = (DateTimeOffset.UtcNow - _start).TotalSeconds;
        bool part = ((int)(t / 8)) % 2 == 0;
        if (((int)t) % 8 == 0)
        {
            _cycles++;
        }

        var reg = new StringBuilder();
        reg.Append("[*NUMREG*]$NUMREG  Storage: CMOS  Access: RW  : ARRAY[200] OF Numeric Reg\r\n SYNTHETIC (animated)\r\n");
        reg.Append(CultureInfo.InvariantCulture, $"  [1] = {_cycles}  'Cycle count'\r\n");
        reg.Append(CultureInfo.InvariantCulture, $"  [2] = {1287 + _cycles}  'Weld count'\r\n");
        reg.Append(CultureInfo.InvariantCulture, $"  [3] = {14.5 + (5 * Math.Sin(t / 30)):0.000000}  'Tip dress interval'\r\n");
        reg.Append(CultureInfo.InvariantCulture, $"  [4] = {38 + (_random.NextDouble() * 4):0.000000}  'Motor current'\r\n");
        _sim.MdOverrides["NUMREG.VA"] = Encoding.Latin1.GetBytes(reg.ToString());

        var io = new StringBuilder();
        io.Append("SYNTHETIC (animated)\r\n");
        io.Append(CultureInfo.InvariantCulture, $"DI[  1] {(part ? "ON " : "OFF")}  Part present\r\n");
        io.Append("DI[  2] ON   Clamp closed\r\n");
        io.Append(CultureInfo.InvariantCulture, $"DO[  1] {(part ? "ON " : "OFF")}  Gun close\r\n");
        io.Append(CultureInfo.InvariantCulture, $"GI[  1] {_cycles % 16}  Style\r\n");
        _sim.MdOverrides["IOSTATE.DG"] = Encoding.Latin1.GetBytes(io.ToString());

        _sim.KclOverrides["SHOW VAR $TIMER[1].$TIMER_VAL"] =
            string.Create(CultureInfo.InvariantCulture, $"$TIMER[1].$TIMER_VAL  Storage: CMOS  Access: RW  : INTEGER = {(int)(t * 1000)}");
    }
}
