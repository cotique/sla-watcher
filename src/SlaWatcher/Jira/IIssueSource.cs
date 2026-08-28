namespace SlaWatcher.Jira;

/// <summary>
/// Everything the service is allowed to know about the tracker.
///
/// <para>
/// Keys and timestamps, and nothing else. Summaries, descriptions, comments and the people
/// attached to them are never requested, so there is no field to forget to strip before a log
/// line. A guarantee made by the shape of an interface outlasts one made by a rule.
/// </para>
/// <para>
/// It is an interface so that the failure cases have somewhere to come from. The double
/// injects them at the container level; a fake handler injects them in a unit test.
/// </para>
/// </summary>
public interface IIssueSource
{
    /// <summary>
    /// Keys of the issues that changed inside the window, oldest change first.
    /// </summary>
    Task<IReadOnlyList<string>> FindIssueKeysAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every status change on one issue, oldest first.
    ///
    /// <para>
    /// A separate call per issue on purpose. Asking search to expand the changelog truncates
    /// it once the history is longer than the page, and a truncated history silently shortens
    /// the age it was fetched to measure.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<StatusTransition>> GetStatusTransitionsAsync(
        string issueKey,
        CancellationToken cancellationToken);
}
