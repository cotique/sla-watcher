namespace SlaWatcher;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// Bound and validated at startup. A missing connection string has to fail at boot,
/// not on the first fire at three in the morning.
/// </summary>
public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    /// <summary>The database named in the connection string is the one the job store uses.</summary>
    [Required(AllowEmptyStrings = false)]
    public string MongoConnectionString { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string InstanceName { get; init; } = "sla-watcher";

    /// <summary>
    /// Identity of this process in the store. AUTO is ignored while the store reports
    /// Clustered = false: Quartz then writes NON_CLUSTERED, and two processes share one
    /// scheduler row and one identity. Set it explicitly, differently per process.
    /// </summary>
    public string InstanceId { get; init; } = "AUTO";

    /// <summary>
    /// How long the job pretends to work. Zero for normal operation. A non-zero value is a
    /// test instrument: it holds the execution open long enough to kill the process while it
    /// is mid-flight, which is the only way to observe what happens to a lock whose holder
    /// dies. The window is milliseconds otherwise.
    /// </summary>
    public int WorkSeconds { get; init; }

    /// <summary>
    /// Stop the host after this many seconds. Zero means run until stopped. A test
    /// instrument: Windows gives no way to send a console app a graceful interrupt from a
    /// script, and a hard kill produces no shutdown at all, which is the part worth watching.
    /// </summary>
    public int RunSeconds { get; init; }

    /// <summary>
    /// Megabytes of native memory the job takes and touches before it finishes. Zero for
    /// normal operation. A test instrument for the kernel to act on: managed allocations get
    /// answered with OutOfMemoryException, which the process survives, and surviving is the
    /// opposite of what the test needs.
    /// </summary>
    public int AllocateMb { get; init; }

    /// <summary>Optional. Keeps the store's collections apart from anything else in the database.</summary>
    public string CollectionPrefix { get; init; } = "quartz";

    /// <summary>
    /// Cron for the polling trigger, in configuration rather than as a literal in code.
    /// One minute is the floor: below it Quartz paces on its idle wait and silently drops
    /// slots. See the cron rule in .claude/audit-rules/dotnet.md.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string TickCron { get; init; } = "0 * * * * ?";
}
