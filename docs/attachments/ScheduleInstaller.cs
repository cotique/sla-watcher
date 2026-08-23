namespace SlaWatcher;
using Microsoft.Extensions.Options;
using Quartz;

/// <summary>
/// Installs the schedule after the scheduler is up, idempotently, so that several instances
/// starting at once against an empty database all succeed.
/// </summary>
/// <remarks>
/// Declaring the job and trigger inside <c>AddQuartz</c> does not survive that. Quartz's
/// initialisation reads the schedule, asks whether each job already exists, and then calls
/// <c>ScheduleJob(job, trigger)</c> for the ones that did not. Two processes starting together on
/// an empty database both get "does not exist", both call it, and the second one is answered with
/// <c>ObjectAlreadyExistsException</c>. That is the job store keeping its contract, since
/// <c>IJobStore.StoreJobAndTrigger</c> is defined to refuse duplicates. The unhandled exception
/// takes the host down with it, so a cold start of a fresh deployment loses an instance.
/// <para>
/// <c>OverWriteExistingData</c> does not help: it is already true by default, and it only changes
/// what happens when the duplicate is visible at the moment of the check. This is the window after
/// it.
/// </para>
/// </remarks>
public sealed class ScheduleInstaller : IHostedService
{
    private static readonly JobKey Tick = new("tick");
    private static readonly TriggerKey TickTrigger = new("tick-trigger");

    private readonly ISchedulerFactory _schedulerFactory;
    private readonly SchedulerOptions _options;
    private readonly ILogger<ScheduleInstaller> _logger;

    public ScheduleInstaller(ISchedulerFactory schedulerFactory, IOptions<SchedulerOptions> options,
        ILogger<ScheduleInstaller> logger)
    {
        _schedulerFactory = schedulerFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

        var job = JobBuilder.Create<TickJob>()
            .WithIdentity(Tick)
            .WithDescription("Skeleton heartbeat")
            .StoreDurably()
            .Build();

        var trigger = TriggerBuilder.Create()
            .ForJob(Tick)
            .WithIdentity(TickTrigger)
            .WithCronSchedule(_options.TickCron, x => x.WithMisfireHandlingInstructionDoNothing())
            .Build();

        // Three attempts, because each retry closes the window the previous one lost: once another
        // instance's write has landed, it is visible to the check and the reschedule path takes it.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // replace: true is an upsert in the store, so this half needs no retry of its own.
                await scheduler.AddJob(job, replace: true, storeNonDurableWhileAwaitingScheduling: true,
                    cancellationToken);

                if (await scheduler.CheckExists(TickTrigger, cancellationToken))
                {
                    await scheduler.RescheduleJob(TickTrigger, trigger, cancellationToken);
                }
                else
                {
                    await scheduler.ScheduleJob(trigger, cancellationToken);
                }

                _logger.LogInformation("Schedule installed on attempt {Attempt}", attempt);
                return;
            }
            catch (ObjectAlreadyExistsException) when (attempt < 3)
            {
                _logger.LogInformation(
                    "Another instance installed the schedule while this one was writing it; retrying");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
