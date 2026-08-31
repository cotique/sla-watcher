namespace SlaWatcher.Tests;
using MongoDB.Bson;
using MongoDB.Driver;

/// <summary>
/// The probe against a real MongoDB, because what it has to be right about is a query and a
/// document shape, and neither survives being mocked.
/// <para>
/// The fired-trigger documents are written here by hand, in the shape the job store actually
/// writes. That is the point: the probe reads another package's collection, so the test has to
/// state the shape it depends on rather than inherit it, and a store upgrade that changes the
/// shape fails here instead of failing silently in production.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class StuckExecutionProbeTests : IAsyncLifetime
{
    private const string InstanceName = "sla-watcher-tests";
    private const string Prefix = "quartz";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("SLA_WATCHER_TEST_MONGO") ??
        "mongodb://localhost:27117/sla-watcher-tests?serverSelectionTimeoutMS=3000";

    private static readonly DateTimeOffset Now = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    private IMongoCollection<BsonDocument> _firedTriggers = null!;
    private StuckExecutionProbe _probe = null!;

    public async Task InitializeAsync()
    {
        var url = MongoUrl.Create(ConnectionString);
        var database = new MongoClient(url).GetDatabase(url.DatabaseName);
        await database.DropCollectionAsync($"{Prefix}.firedTriggers");
        _firedTriggers = database.GetCollection<BsonDocument>($"{Prefix}.firedTriggers");
        _probe = new StuckExecutionProbe(ConnectionString, Prefix, InstanceName);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ReportsAnExecutionOlderThanTheThreshold()
    {
        await Insert("pod-a", firedMinutesAgo: 40, state: "Executing");

        var stuck = await _probe.FindAsync(Now, TimeSpan.FromMinutes(15), CancellationToken.None);

        var only = Assert.Single(stuck);
        Assert.Equal("DEFAULT.tick-trigger", only.TriggerKey);
        Assert.Equal("pod-a", only.InstanceId);
        Assert.Equal(TimeSpan.FromMinutes(40), only.RunningFor);
    }

    [Fact]
    public async Task LeavesAnExecutionThatHasNotRunLongEnough()
    {
        // The one that has to keep passing. A watchdog that fires on healthy work is a
        // watchdog somebody switches off.
        await Insert("pod-a", firedMinutesAgo: 5, state: "Executing");

        var stuck = await _probe.FindAsync(Now, TimeSpan.FromMinutes(15), CancellationToken.None);

        Assert.Empty(stuck);
    }

    [Fact]
    public async Task IgnoresAnAcquiredTriggerThatIsNotExecuting()
    {
        // Acquired and Blocked records are ordinary scheduling states and age harmlessly.
        await Insert("pod-a", firedMinutesAgo: 90, state: "Acquired");

        var stuck = await _probe.FindAsync(Now, TimeSpan.FromMinutes(15), CancellationToken.None);

        Assert.Empty(stuck);
    }

    [Fact]
    public async Task IgnoresAnotherSchedulerSharingTheDatabase()
    {
        await Insert("pod-a", firedMinutesAgo: 90, state: "Executing", instanceName: "someone-else");

        var stuck = await _probe.FindAsync(Now, TimeSpan.FromMinutes(15), CancellationToken.None);

        Assert.Empty(stuck);
    }

    [Fact]
    public async Task ReportsEveryStuckExecutionRatherThanTheFirst()
    {
        await Insert("pod-a", firedMinutesAgo: 40, state: "Executing");
        await Insert("pod-b", firedMinutesAgo: 70, state: "Executing", fireInstanceId: "pod-b-2");

        var stuck = await _probe.FindAsync(Now, TimeSpan.FromMinutes(15), CancellationToken.None);

        Assert.Equal(2, stuck.Count);
        Assert.Contains(stuck, execution => execution.InstanceId == "pod-a");
        Assert.Contains(stuck, execution => execution.InstanceId == "pod-b");
    }

    private Task Insert(
        string instanceId,
        int firedMinutesAgo,
        string state,
        string instanceName = InstanceName,
        string? fireInstanceId = null) =>
        _firedTriggers.InsertOneAsync(new BsonDocument
        {
            {
                "_id", new BsonDocument
                {
                    { "InstanceName", instanceName },
                    { "FiredInstanceId", fireInstanceId ?? $"{instanceId}-1" },
                }
            },
            {
                "TriggerKey", new BsonDocument
                {
                    { "Name", "tick-trigger" },
                    { "Group", "DEFAULT" },
                }
            },
            { "InstanceId", instanceId },
            { "Fired", Now.AddMinutes(-firedMinutesAgo).UtcDateTime },
            { "State", state },
        });
}
