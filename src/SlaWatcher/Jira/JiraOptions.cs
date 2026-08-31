namespace SlaWatcher.Jira;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// How to query the tracker. Not where, and not as whom: the base address and the credential
/// are put on the <see cref="HttpClient" /> when it is registered, so neither reaches this
/// type and neither can be logged from it.
/// </summary>
public sealed class JiraOptions
{
    public const string SectionName = "Jira";

    public const int DefaultPageSize = 50;
    public const int DefaultMaxPages = 200;
    public const int DefaultMaxRetries = 3;
    public const int DefaultFallbackRetryAfterSeconds = 5;

    /// <summary>The tracker refuses a page larger than this, so asking for more wastes a call.</summary>
    private const int LargestPageTheTrackerServes = 100;

    private const int SmallestPageWorthAsking = 1;
    private const int SmallestUsefulPageCeiling = 1;
    private const int PageCeilingBeyondWhichNoWindowIsPlausible = 10_000;
    private const int NoRetriesAtAll = 0;
    private const int MostRetriesWorthWaitingThrough = 10;
    private const int ShortestFallbackWaitSeconds = 1;
    private const int LongestFallbackWaitSeconds = 300;

    /// <summary>
    /// The scope of the search, without a time window and without ordering. Deployment
    /// specific, so it lives in local configuration and never in the repository: it names a
    /// project and a status that are not ours to publish.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Jql { get; init; } = string.Empty;

    /// <summary>Issues per search page.</summary>
    [Range(SmallestPageWorthAsking, LargestPageTheTrackerServes)]
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>
    /// A ceiling on pages, per call. A server that keeps answering with a next page turns an
    /// unbounded loop into a hang, and the double can be made to do exactly that.
    /// </summary>
    [Range(SmallestUsefulPageCeiling, PageCeilingBeyondWhichNoWindowIsPlausible)]
    public int MaxPages { get; init; } = DefaultMaxPages;

    /// <summary>
    /// How many times one request may be retried after a throttle response. Beyond this the
    /// call fails rather than waiting indefinitely on a tracker that has decided to say no.
    /// </summary>
    [Range(NoRetriesAtAll, MostRetriesWorthWaitingThrough)]
    public int MaxRetries { get; init; } = DefaultMaxRetries;

    /// <summary>
    /// Used when a throttle response arrives without a <c>Retry-After</c> header. A guess,
    /// and named as one: the header is what is honoured whenever it is present.
    /// </summary>
    [Range(ShortestFallbackWaitSeconds, LongestFallbackWaitSeconds)]
    public int FallbackRetryAfterSeconds { get; init; } = DefaultFallbackRetryAfterSeconds;
}
