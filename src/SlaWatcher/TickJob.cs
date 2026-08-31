namespace SlaWatcher;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using Quartz;

/// <summary>
/// The skeleton's only job. It does no product work: it records that it ran, so two
/// clustered instances can be checked against each other.
///
/// <see cref="DisallowConcurrentExecutionAttribute"/> is here from the start. The real job
/// will touch the watermark, and two overlapping runs would read the same one and process
/// the same page.
/// </summary>
[DisallowConcurrentExecution]
public sealed class TickJob : IJob
{
    private readonly ILogger<TickJob> _logger;
    private readonly FireLog _fireLog;
    private readonly SchedulerOptions _options;

    public TickJob(ILogger<TickJob> logger, FireLog fireLog, IOptions<SchedulerOptions> options)
    {
        _logger = logger;
        _fireLog = fireLog;
        _options = options.Value;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var schedulerInstanceId = context.Scheduler.SchedulerInstanceId;

        // The fire instance id goes on every line a job writes. Without it, two runs
        // interleaved in one log cannot be told apart afterwards.
        _logger.LogInformation(
            "Tick {FireInstanceId} on {SchedulerInstanceId} scheduled for {ScheduledFireTimeUtc:O}, fired {FireTimeUtc:O}, refires {RefireCount}",
            context.FireInstanceId,
            schedulerInstanceId,
            context.ScheduledFireTimeUtc,
            context.FireTimeUtc,
            context.RefireCount);

        if (_options.WorkSeconds > 0)
        {
            // Deliberate, and only ever set in a test run: keeps the execution open so the
            // process can be killed while it holds whatever the store is holding.
            _logger.LogInformation("Tick {FireInstanceId} holding for {WorkSeconds}s",
                context.FireInstanceId, _options.WorkSeconds);
            await Task.Delay(TimeSpan.FromSeconds(_options.WorkSeconds), context.CancellationToken);
        }

        if (_options.AllocateMb > 0)
        {
            // Native, and written to page by page. Anything managed would be refused by the
            // runtime as OutOfMemoryException before the cgroup ever noticed; the point here
            // is to be killed, not to be told no.
            _logger.LogInformation("Tick {FireInstanceId} taking {AllocateMb}MB of native memory",
                context.FireInstanceId, _options.AllocateMb);
            for (var block = 0; block < _options.AllocateMb; block++)
            {
                var memory = Marshal.AllocHGlobal(1024 * 1024);
                for (var offset = 0; offset < 1024 * 1024; offset += 4096)
                {
                    Marshal.WriteByte(memory, offset, 1);
                }
            }
        }

        var recorded = await _fireLog.RecordAsync(
            context.Trigger.Key.ToString(),
            context.ScheduledFireTimeUtc,
            schedulerInstanceId,
            context.FireInstanceId,
            context.CancellationToken);

        if (!recorded)
        {
            // The slot was already recorded, so another instance ran it. Worth a warning
            // rather than silence: with the store's recovery in play this is how a
            // reassigned slot that its original owner also finished shows up.
            _logger.LogWarning(
                "Slot {ScheduledFireTimeUtc:O} was already recorded; this execution on {SchedulerInstanceId} is a duplicate",
                context.ScheduledFireTimeUtc,
                schedulerInstanceId);
        }
    }
}
