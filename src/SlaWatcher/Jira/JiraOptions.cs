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

    /// <summary>
    /// The scope of the search, without a time window and without ordering. Deployment
    /// specific, so it lives in local configuration and never in the repository: it names a
    /// project and a status that belong to the employer.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Jql { get; init; } = string.Empty;

    /// <summary>Issues per search page.</summary>
    [Range(1, 100)]
    public int PageSize { get; init; } = 50;

    /// <summary>
    /// A ceiling on pages, per call. A server that keeps answering with a next page turns an
    /// unbounded loop into a hang, and the double can be made to do exactly that.
    /// </summary>
    [Range(1, 10_000)]
    public int MaxPages { get; init; } = 200;

    /// <summary>
    /// How many times one request may be retried after a throttle response. Beyond this the
    /// call fails rather than waiting indefinitely on a tracker that has decided to say no.
    /// </summary>
    [Range(0, 10)]
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Used when a throttle response arrives without a <c>Retry-After</c> header. A guess,
    /// and named as one: the header is what is honoured whenever it is present.
    /// </summary>
    [Range(1, 300)]
    public int FallbackRetryAfterSeconds { get; init; } = 5;
}
