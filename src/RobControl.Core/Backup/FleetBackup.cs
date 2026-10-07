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

    /// <summary>
    /// Backs up <paramref name="robots"/>, then - when <paramref name="retries"/> is above zero - waits
    /// <paramref name="retryDelay"/> and tries again every robot whose backup failed or came back
    /// partial. A cancelled backup is never retried. The result per robot is its last attempt; every
    /// attempt is its own folder and its own event, so a retry that worked does not hide the failure
    /// before it.
    ///
    /// <para>Why retry at all: a robot that is mid-power-cycle, on a switch being replaced, or
    /// briefly busy answering a pendant is a common reason for one scheduled backup to fail at 3 am.
    /// One retry a few minutes later turns most of those into a good backup instead of a stale robot.</para>
    /// </summary>
    public async Task<IReadOnlyList<BackupResult>> RunAsync(
        IEnumerable<Robot> robots,
        BackupOptions? options = null,
        int concurrency = 2,
        Func<Robot, ControllerIdentity?>? knownIdentity = null,
        IProgress<BackupProgress>? progress = null,
        Action<Robot, BackupResult>? finished = null,
        CancellationToken cancellationToken = default,
        int retries = 0,
        TimeSpan? retryDelay = null,
        Action<IReadOnlyList<Robot>, TimeSpan>? retrying = null)
    {
        ArgumentNullException.ThrowIfNull(robots);
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(retries);

        // De-duplicated by address: two rows for one controller would mean two sessions on it.
        Robot[] list = [.. robots.DistinctBy(r => r.Address)];
        var results = new BackupResult[list.Length];
        await PassAsync(list, Enumerable.Range(0, list.Length)).ConfigureAwait(false);

        for (int attempt = 0; attempt < retries; attempt++)
        {
            int[] again = [.. Enumerable.Range(0, list.Length).Where(i => IsWorthRetrying(results[i].Outcome))];
            if (again.Length == 0)
            {
                break;
            }

            TimeSpan delay = retryDelay ?? TimeSpan.FromMinutes(2);
            retrying?.Invoke([.. again.Select(i => list[i])], delay);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            await PassAsync(list, again).ConfigureAwait(false);
        }

        return results;

        async Task PassAsync(Robot[] all, IEnumerable<int> indexes)
        {
            using var gate = new SemaphoreSlim(concurrency, concurrency);
            await Task.WhenAll(indexes.Select(async i =>
            {
                Robot robot = all[i];
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
        }
    }

    /// <summary>Failed or partial: something on the network or the controller that may well be gone in a few minutes.</summary>
    public static bool IsWorthRetrying(BackupOutcome outcome) => outcome is BackupOutcome.Failed or BackupOutcome.Partial;
}
