using RobControl.Core.Controllers;
using RobControl.Core.Kcl;
using RobControl.Core.Transports.Http;

namespace RobControl.Core.Trending;

/// <summary>
/// Reads a set of signals from one robot, as few requests as possible:
/// <list type="bullet">
///   <item>every register in one GET of <c>/MD/NUMREG.VA</c>,</item>
///   <item>all I/O in one GET of <c>/MD/IOSTATE.DG</c>,</item>
///   <item>one KCL <c>SHOW VAR</c> per system variable - which is why those cost more and the UI says so.</item>
/// </list>
/// <para>GET only, through <see cref="ControllerWebClient"/>, which serialises requests per robot.
/// A failure in one source is reported against its signals and does not stop the others.</para>
/// </summary>
public static class RobotSampler
{
    public const string RegisterFile = "NUMREG.VA";
    public const string IoFile = "IOSTATE.DG";

    public static async Task<SampleRead> ReadAsync(ControllerWebClient web, IReadOnlyCollection<SignalAddress> signals, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(web);
        ArgumentNullException.ThrowIfNull(signals);

        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        var problems = new Dictionary<string, string>(StringComparer.Ordinal);

        List<SignalAddress> registers = [.. signals.Where(s => s.Kind == SignalKind.NumericRegister)];
        List<SignalAddress> io = [.. signals.Where(s => s.Kind == SignalKind.Io)];
        List<SignalAddress> variables = [.. signals.Where(s => s.Kind == SignalKind.SystemVariable)];

        if (registers.Count > 0)
        {
            await FromFileAsync(web, RegisterFile, registers, RegisterFileParser.Parse, a => a.Index!.Value, values, problems, cancellationToken)
                .ConfigureAwait(false);
        }

        if (io.Count > 0)
        {
            await FromFileAsync(web, IoFile, io, IoStateParser.Parse, a => a.Text, values, problems, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (SignalAddress variable in variables)
        {
            try
            {
                KclResult result = await web.KclAsync("SHOW VAR " + variable.Text, cancellationToken).ConfigureAwait(false);
                if (result.Error is { } error)
                {
                    problems[variable.Text] = error;
                }
                else if (ScalarParser.TryParse(ShowVarParser.Value(result.Output), out double value))
                {
                    values[variable.Text] = value;
                }
                else
                {
                    problems[variable.Text] = "Not a single number: " + (ShowVarParser.Value(result.Output) ?? result.Output);
                }
            }
            catch (HttpResourceLockedException ex)
            {
                // Every remaining variable would hit the same lock - say so once for all of them.
                foreach (SignalAddress rest in variables.Where(v => !values.ContainsKey(v.Text) && !problems.ContainsKey(v.Text)))
                {
                    problems[rest.Text] = ex.Message;
                }

                break;
            }
            catch (RobControlException ex)
            {
                problems[variable.Text] = ex.Message;
            }
        }

        return new SampleRead(DateTimeOffset.UtcNow, values, problems);
    }

    private static async Task FromFileAsync<TKey>(
        ControllerWebClient web,
        string file,
        List<SignalAddress> wanted,
        Func<string, IReadOnlyDictionary<TKey, double>> parse,
        Func<SignalAddress, TKey> key,
        Dictionary<string, double> values,
        Dictionary<string, string> problems,
        CancellationToken cancellationToken)
        where TKey : notnull
    {
        string? failure = null;
        IReadOnlyDictionary<TKey, double>? parsed = null;
        try
        {
            HttpExchange exchange = await web.GetDiagnosticFileAsync(file, cancellationToken).ConfigureAwait(false);
            if (exchange.IsSuccess)
            {
                parsed = parse(exchange.Text);
            }
            else
            {
                failure = $"{file} answered with status {exchange.StatusCode}.";
            }
        }
        catch (RobControlException ex)
        {
            failure = ex.Message;
        }

        foreach (SignalAddress address in wanted)
        {
            if (parsed is not null && parsed.TryGetValue(key(address), out double value))
            {
                values[address.Text] = value;
            }
            else
            {
                problems[address.Text] = failure ?? $"{address.Text} is not in {file}.";
            }
        }
    }
}
