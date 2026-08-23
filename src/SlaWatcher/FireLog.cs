namespace SlaWatcher;
using MongoDB.Bson;
using MongoDB.Driver;

/// <summary>
/// One document per executed slot, keyed by the slot.
///
/// Not one per attempt. A write that reports failure may still have been applied — measured
/// on 2026-08-23, a thawed pod's insert threw <c>SocketException</c> while the document sat
/// in the collection — so a retry has to collide rather than add a second row. An identifier
/// generated per attempt collides with nothing.
///
/// The key is the primary key, so uniqueness is enforced by the store itself and needs no
/// second index. A duplicate then arrives as a write error with code 11000, which is a fact
/// worth acting on, instead of a row nobody notices.
/// </summary>
public sealed class FireLog
{
    private const int DuplicateKey = 11000;

    private readonly IMongoCollection<BsonDocument> _fires;

    public FireLog(string connectionString)
    {
        var url = MongoUrl.Create(connectionString);
        var database = new MongoClient(url).GetDatabase(url.DatabaseName);
        _fires = database.GetCollection<BsonDocument>("fires");
    }

    /// <returns>
    /// True when this call recorded the slot, false when someone had already recorded it.
    /// The caller decides what that means; here it means another instance ran the same slot,
    /// which is the thing worth knowing.
    /// </returns>
    public async Task<bool> RecordAsync(
        string triggerKey,
        DateTimeOffset? scheduledFireTimeUtc,
        string schedulerInstanceId,
        string fireInstanceId,
        CancellationToken cancellationToken)
    {
        // Derived from the slot and the trigger, so two attempts at the same slot produce the
        // same key. Round-trip format, because a key that depends on the current culture is
        // not deterministic across machines.
        var slot = scheduledFireTimeUtc?.UtcDateTime;
        var id = $"{triggerKey}:{slot?.ToString("O") ?? "unscheduled"}";

        var document = new BsonDocument
        {
            { "_id", id },
            { "scheduledFireTimeUtc", slot ?? (BsonValue)BsonNull.Value },
            { "schedulerInstanceId", schedulerInstanceId },
            { "fireInstanceId", fireInstanceId },
            { "recordedAtUtc", DateTime.UtcNow },
        };

        try
        {
            await _fires.InsertOneAsync(document, options: null, cancellationToken);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == DuplicateKey)
        {
            return false;
        }
    }
}
