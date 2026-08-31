namespace SlaWatcher.Jira;

/// <summary>
/// One move of an issue from one status to another, as the tracker recorded it.
///
/// <para>
/// Statuses are carried as names because that is what the changelog stores and what a person
/// configures the band against. Nothing else from the entry is kept: an author moved a ticket,
/// and who they are is transit-only.
/// </para>
/// </summary>
public sealed record StatusTransition(DateTimeOffset AtUtc, string FromStatus, string ToStatus);
