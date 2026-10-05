using RobControl.Core.Controllers;
using RobControl.Core.Robots;

namespace RobControl.Core.Backup;

/// <summary>
/// Backs up several robots, a few at a time.
///
/// <para>Concurrency is across robots, never within one: each robot gets one FTP session at a time
/// (CLAUDE.md, Safety). The default of two keeps a fleet backup from saturating a plant uplink or
/// a laptop's one NIC; it is a setting, not a constant.</para>
/// </summary>
public sealed class FleetBackup(BackupRunner runner)
{
    private readonly BackupRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    public async Task<IReadOnlyList<BackupResult>> RunAsync(
        IEnumerable<Robot> robots,
        BackupOptions? options = null,
        int concurrency = 2,
        Func<Robot, ControllerIdentity?>? knownIdentity = null,
        IProgress<BackupProgress>? progress = null,
        Action<Robot, BackupResult>? finished = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(robots);
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);

        // De-duplicated by address: two rows for one controller would mean two sessions on it.
        Robot[] list = [.. robots.DistinctBy(r => r.Address)];
        var results = new BackupResult[list.Length];
        using var gate = new SemaphoreSlim(concurrency, concurrency);

        await Task.WhenAll(list.Select(async (robot, i) =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                results[i] = await _runner.RunAsync(robot, options, knownIdentity?.Invoke(robot), progress, cancellationToken)
                    .ConfigureAwait(false);
                finished?.Invoke(robot, results[i]);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        return results;
    }
}
