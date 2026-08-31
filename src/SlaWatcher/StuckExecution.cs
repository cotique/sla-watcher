namespace SlaWatcher;

/// <summary>
/// An execution the job store still believes is running, long after anything here plausibly
/// could be.
/// <para>
/// Only the identifiers. Whatever the job was doing is not carried, because a watchdog that
/// reports the work rather than the fact of it becomes another place free text can leak.
/// </para>
/// </summary>
public sealed record StuckExecution(
    string TriggerKey,
    string InstanceId,
    DateTimeOffset FiredUtc,
    TimeSpan RunningFor);
