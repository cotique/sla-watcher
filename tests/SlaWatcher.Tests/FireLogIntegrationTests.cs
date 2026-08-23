namespace SlaWatcher.Tests;
using MongoDB.Bson;
using MongoDB.Driver;

/// <summary>
/// Needs a real MongoDB, so it carries the Integration trait and CI never runs it. There is
/// no Skip here on purpose: an integration test whose dependency is absent has to fail. A
/// skip on a missing database turns a red suite green and reports coverage nobody has.
///
/// Its own database, not the bench's. A run that shares state with the experiment is how a
/// result from twenty minutes ago turns up in a supposedly fresh measurement.
/// </summary>
[Trait("Category", "Integration")]
public class FireLogIntegrationTests : IAsyncLifetime
{
    private const string Trigger = "DEFAULT.tick-trigger";

    // A short selection timeout so an absent database fails in seconds rather than sitting on
    // the driver's thirty-second default.
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("SLA_WATCHER_TEST_MONGO") ??
        "mongodb://localhost:27117/sla-watcher-tests?serverSelectionTimeoutMS=3000";

    private IMongoCollection<BsonDocument> _fires = null!;

    public async Task InitializeAsync()
    {
        var url = MongoUrl.Create(ConnectionString);
        var database = new MongoClient(url).GetDatabase(url.DatabaseName);
        await database.DropCollectionAsync("fires");
        _fires = database.GetCollection<BsonDocument>("fires");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TheSecondAttemptAtOneSlotIsRefused()
    {
        var log = new FireLog(ConnectionString);
        var slot = new DateTimeOffset(2026, 8, 23, 19, 35, 0, TimeSpan.Zero);

        var first = await log.RecordAsync(Trigger, slot, "pod-a", "fire-1", CancellationToken.None);

        // A different instance and a different fire instance id, deliberately: this is the
        // shape of a slot reassigned by recovery whose original owner also finishes it.
        var second = await log.RecordAsync(Trigger, slot, "pod-b", "fire-2", CancellationToken.None);

        Assert.True(first, "the first attempt records the slot");
        Assert.False(second, "the second attempt is told the slot was already recorded");
        Assert.Equal(1, await _fires.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task TheRecordedDocumentBelongsToWhoeverWonTheSlot()
    {
        var log = new FireLog(ConnectionString);
        var slot = new DateTimeOffset(2026, 8, 23, 19, 36, 0, TimeSpan.Zero);

        await log.RecordAsync(Trigger, slot, "pod-a", "fire-1", CancellationToken.None);
        await log.RecordAsync(Trigger, slot, "pod-b", "fire-2", CancellationToken.None);

        var document = await _fires.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();

        Assert.Equal(FireLog.SlotKey(Trigger, slot), document["_id"].AsString);
        Assert.Equal("pod-a", document["schedulerInstanceId"].AsString);
    }

    [Fact]
    public async Task SeparateSlotsEachGetARecord()
    {
        var log = new FireLog(ConnectionString);
        var first = new DateTimeOffset(2026, 8, 23, 19, 35, 0, TimeSpan.Zero);

        await log.RecordAsync(Trigger, first, "pod-a", "fire-1", CancellationToken.None);
        await log.RecordAsync(Trigger, first.AddMinutes(1), "pod-a", "fire-2", CancellationToken.None);

        Assert.Equal(2, await _fires.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }
}
