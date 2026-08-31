namespace SlaWatcher;
using Microsoft.Extensions.Options;

/// <summary>
/// Reports executions that have been running too long, on its own timer.
/// <para>
/// Deliberately not a Quartz job. The failure it exists to catch is a job that never finishes,
/// and enough of those exhaust the scheduler's thread pool, at which point a watchdog scheduled
/// by Quartz does not run either. A watchdog that depends on the thing it watches is not a
/// watchdog.
/// </para>
/// <para>
/// The cost of that choice: every instance checks, so a stuck execution is reported once per
/// living instance. For a log line that is closer to a feature than a defect, and the
/// alternative is a distributed lock in the one component that has to keep working when
/// everything else is stuck.
/// </para>
/// </summary>
public sealed class StuckExecutionMonitor : BackgroundService
{
    private readonly StuckExecutionProbe _probe;
    private readonly SchedulerOptions _options;
    private readonly ILogger<StuckExecutionMonitor> _logger;
    private readonly TimeProvider _time;

    public StuckExecutionMonitor(
        StuckExecutionProbe probe,
        IOptions<SchedulerOptions> options,
        ILogger<StuckExecutionMonitor> logger,
        TimeProvider? time = null)
    {
        _probe = probe;
        _options = options.Value;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.StuckExecutionCheckIntervalSeconds);
        var threshold = TimeSpan.FromMinutes(_options.StuckExecutionThresholdMinutes);

        _logger.LogInformation(
            "Watching for executions running longer than {ThresholdMinutes}m, checking every {IntervalSeconds}s",
            _options.StuckExecutionThresholdMinutes, _options.StuckExecutionCheckIntervalSeconds);

        using var timer = new PeriodicTimer(interval, _time);

        // The first check waits one interval. A record left by a crash is still there at
        // startup, and the store's own recovery has not necessarily run yet, so checking
        // immediately reports something that is about to be cleaned up by design.
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var stuck = await _probe
                    .FindAsync(_time.GetUtcNow(), threshold, stoppingToken)
                    .ConfigureAwait(false);

                foreach (var execution in stuck)
                {
                    _logger.LogError(
                        "Execution of {TriggerKey} on {InstanceId} has been running for {RunningForMinutes:F0}m, since {FiredUtc:O}. Nothing else reports this: the schedule is stopped and no health check fails",
                        execution.TriggerKey,
                        execution.InstanceId,
                        execution.RunningFor.TotalMinutes,
                        execution.FiredUtc);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception failure)
            {
                // A watchdog that dies on its first bad round stops watching for good, and
                // says nothing about it. The next tick tries again.
                _logger.LogWarning(failure, "The stuck-execution check failed this round");
            }
        }
    }
}
