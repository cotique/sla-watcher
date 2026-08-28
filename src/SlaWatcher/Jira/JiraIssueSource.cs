namespace SlaWatcher.Jira;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

/// <summary>
/// Reads the tracker over HTTP.
///
/// <para>
/// The JSON this parses is <b>an assumption until the contract test runs</b>. It is not
/// recorded anywhere in this repository as a verified fact, and the double agrees with it by
/// construction, so a green suite here says the client and the double share a belief. What
/// promotes that belief to knowledge is one test against the real instance.
/// </para>
/// </summary>
public sealed class JiraIssueSource : IIssueSource
{
    /// <summary>
    /// Waits before a retry. Injected so a test can assert what the wait would have been
    /// instead of spending it: a test that sleeps is a test nobody runs twice.
    /// </summary>
    public delegate Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);

    /// <summary>Attempts are counted from one so the log needs no arithmetic to read.</summary>
    private const int FirstAttempt = 1;

    /// <summary>Pages likewise, so the ceiling reads as a count of pages rather than an index.</summary>
    private const int FirstPage = 1;

    private readonly HttpClient _http;
    private readonly JiraOptions _options;
    private readonly ILogger<JiraIssueSource> _logger;
    private readonly DelayAsync _delay;

    public JiraIssueSource(
        HttpClient http,
        IOptions<JiraOptions> options,
        ILogger<JiraIssueSource> logger,
        DelayAsync? delay = null)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

    public async Task<IReadOnlyList<string>> FindIssueKeysAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        // Ordered by the field the window filters on, so paging is stable. Without an order
        // the server may return the same issue on two pages and omit another entirely.
        var jql = $"{_options.Jql} AND updated >= \"{Jql(fromUtc)}\" AND updated < \"{Jql(toUtc)}\" ORDER BY updated ASC";

        var keys = new List<string>();
        var startAt = 0;

        for (var page = FirstPage; page <= _options.MaxPages; page++)
        {
            var url = $"rest/api/3/search?jql={Uri.EscapeDataString(jql)}&startAt={startAt}&maxResults={_options.PageSize}&fields=key";

            using var document = await GetAsync(url, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;

            var issues = root.GetProperty("issues");
            foreach (var issue in issues.EnumerateArray())
            {
                keys.Add(issue.GetProperty("key").GetString()!);
            }

            startAt += issues.GetArrayLength();

            // An empty page ends the walk even when the reported total disagrees. Trusting
            // total alone loops forever against a server that reports one it cannot fill.
            if (issues.GetArrayLength() == 0 || startAt >= root.GetProperty("total").GetInt32())
            {
                return keys;
            }

            if (page == _options.MaxPages)
            {
                throw new InvalidOperationException(
                    $"Search stopped at the page ceiling of {_options.MaxPages} with {keys.Count} issues collected. Either the window is far larger than intended or the server is paging without end.");
            }
        }

        return keys;
    }

    public async Task<IReadOnlyList<StatusTransition>> GetStatusTransitionsAsync(
        string issueKey, CancellationToken cancellationToken)
    {
        var transitions = new List<StatusTransition>();
        var startAt = 0;

        for (var page = FirstPage; page <= _options.MaxPages; page++)
        {
            var url = $"rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/changelog?startAt={startAt}&maxResults={_options.PageSize}";

            using var document = await GetAsync(url, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;

            var entries = root.GetProperty("values");
            foreach (var entry in entries.EnumerateArray())
            {
                var at = ParseInstant(entry.GetProperty("created").GetString()!);

                foreach (var item in entry.GetProperty("items").EnumerateArray())
                {
                    // Only status moves. A changelog entry carries every field that changed in
                    // one edit, and the rest of them are none of this service's business.
                    if (!string.Equals(item.GetProperty("field").GetString(), "status",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    transitions.Add(new StatusTransition(
                        at.ToUniversalTime(),
                        item.GetProperty("fromString").GetString() ?? string.Empty,
                        item.GetProperty("toString").GetString() ?? string.Empty));
                }
            }

            startAt += entries.GetArrayLength();

            if (entries.GetArrayLength() == 0 || startAt >= root.GetProperty("total").GetInt32())
            {
                // Oldest first, so pairing an entry with its exit is a walk rather than a sort
                // at every use.
                transitions.Sort((left, right) => left.AtUtc.CompareTo(right.AtUtc));
                return transitions;
            }

            if (page == _options.MaxPages)
            {
                throw new InvalidOperationException(
                    $"The changelog of one issue exceeded the page ceiling of {_options.MaxPages}.");
            }
        }

        return transitions;
    }

    private async Task<JsonDocument> GetAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = FirstAttempt; ; attempt++)
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt <= _options.MaxRetries)
            {
                var wait = RetryAfter(response);

                // The issue key is in the URL and nothing else is, so this line carries no
                // free text and no person.
                _logger.LogWarning(
                    "Throttled by the tracker, waiting {WaitSeconds}s before retry {Retry} of {MaxRetries}",
                    wait.TotalSeconds, attempt, _options.MaxRetries);

                await _delay(wait, cancellationToken).ConfigureAwait(false);
                continue;
            }

            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonDocument.Parse(payload);
        }
    }

    /// <summary>
    /// What the server asked to be waited, honoured in both forms it may take. A fixed delay
    /// instead would be a guess dressed as compliance.
    /// </summary>
    private TimeSpan RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;

        if (header?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (header?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until;
            }
        }

        return TimeSpan.FromSeconds(_options.FallbackRetryAfterSeconds);
    }

    /// <summary>JQL wants minute precision and its own quoting; seconds are not accepted.</summary>
    private static string Jql(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// The tracker writes its offset without a colon, as <c>+0000</c>. That is legal ISO 8601
    /// basic format and <see cref="JsonElement.GetDateTimeOffset" /> rejects it outright, with
    /// a FormatException naming nothing, so the string is taken raw and parsed here instead.
    ///
    /// <para>
    /// Invariant culture, for the same reason the slot key is formatted round-trip: a parse
    /// that depends on the machine's culture gives two answers on two machines.
    /// </para>
    /// </summary>
    private static DateTimeOffset ParseInstant(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
