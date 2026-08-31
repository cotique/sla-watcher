namespace SlaWatcher;
using MongoDB.Bson;
using MongoDB.Driver;

/// <summary>
/// Asks the job store's own collection which executions have been running too long.
/// <para>
/// This reads a collection that belongs to another package, which is coupling and is worth
/// saying out loud. It is read-only, and nothing else exposes the fact at all: an execution
/// that never finishes produces no error, no log line and no failed health check, and the
/// schedule simply stops. The cost is that a change to the store's document shape breaks this
/// silently, so the integration test writes the document itself rather than trusting a
/// remembered shape.
/// </para>
/// <para>
/// What this catches is precisely what the store's recovery cannot. From 2.2.0 the store
/// reclaims the work of an instance that stopped checking in. An instance that is alive and
/// checking in, with a job wedged inside it on a socket read or a lock, never looks failed and
/// is never reclaimed.
/// </para>
/// </summary>
public sealed class StuckExecutionProbe
{
    private const string ExecutingState = "Executing";

    private readonly IMongoCollection<BsonDocument> _firedTriggers;
    private readonly string _instanceName;

    public StuckExecutionProbe(string connectionString, string collectionPrefix, string instanceName)
    {
        var url = MongoUrl.Create(connectionString);
        var database = new MongoClient(url).GetDatabase(url.DatabaseName);
        _firedTriggers = database.GetCollection<BsonDocument>($"{collectionPrefix}.firedTriggers");
        _instanceName = instanceName;
    }

    /// <summary>
    /// Executions that started before <paramref name="nowUtc" /> minus <paramref name="threshold" />
    /// and are still marked as running.
    /// <para>
    /// The current time is a parameter rather than read inside, so a test can place a record
    /// either side of the boundary without waiting for a clock.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<StuckExecution>> FindAsync(
        DateTimeOffset nowUtc, TimeSpan threshold, CancellationToken cancellationToken)
    {
        var cutoff = nowUtc - threshold;

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id.InstanceName", _instanceName),
            Builders<BsonDocument>.Filter.Eq("State", ExecutingState),
            Builders<BsonDocument>.Filter.Lt("Fired", cutoff.UtcDateTime));

        var documents = await _firedTriggers.Find(filter).ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var stuck = new List<StuckExecution>(documents.Count);

        foreach (var document in documents)
        {
            var fired = DateTime.SpecifyKind(document["Fired"].ToUniversalTime(), DateTimeKind.Utc);
            var triggerKey = document["TriggerKey"].AsBsonDocument;

            stuck.Add(new StuckExecution(
                $"{triggerKey["Group"].AsString}.{triggerKey["Name"].AsString}",
                document["InstanceId"].AsString,
                fired,
                nowUtc - fired));
        }

        return stuck;
    }
}
