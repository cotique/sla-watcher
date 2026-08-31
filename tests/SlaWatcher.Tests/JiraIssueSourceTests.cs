namespace SlaWatcher.Tests;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SlaWatcher.Jira;

/// <summary>
/// The client against a scripted server. No infrastructure, so these run in CI, and no real
/// waiting, so a throttle test costs milliseconds.
///
/// Issue keys here are deliberately of a shape the real tracker cannot produce.
/// </summary>
public class JiraIssueSourceTests
{
    [Fact]
    public async Task PagesUntilTheReportedTotalIsReached()
    {
        var handler = new ScriptedHandler(
            SearchPage(total: 3, startAt: 0, keys: ["DOUBLE-1", "DOUBLE-2"]),
            SearchPage(total: 3, startAt: 2, keys: ["DOUBLE-3"]));

        var keys = await Source(handler).FindIssueKeysAsync(Window.From, Window.To, default);

        Assert.Equal(["DOUBLE-1", "DOUBLE-2", "DOUBLE-3"], keys);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task AnEmptyPageEndsTheWalkEvenWhenTheTotalDisagrees()
    {
        // A server that reports more than it will ever hand over. Trusting the total alone
        // loops here forever.
        var handler = new ScriptedHandler(
            SearchPage(total: 99, startAt: 0, keys: ["DOUBLE-1"]),
            SearchPage(total: 99, startAt: 1, keys: []));

        var keys = await Source(handler).FindIssueKeysAsync(Window.From, Window.To, default);

        Assert.Equal(["DOUBLE-1"], keys);
    }

    [Fact]
    public async Task PagingStopsAtTheCeilingRatherThanHanging()
    {
        // Always one more page, never the last.
        var handler = new ScriptedHandler(_ => SearchPage(total: 1000, startAt: 0, keys: ["DOUBLE-1"]));

        var source = Source(handler, maxPages: 4);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.FindIssueKeysAsync(Window.From, Window.To, default));

        Assert.Contains("page ceiling of 4", failure.Message);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task WaitsExactlyWhatRetryAfterAsksFor()
    {
        var handler = new ScriptedHandler(
            Throttled(retryAfterSeconds: 7),
            SearchPage(total: 1, startAt: 0, keys: ["DOUBLE-1"]));

        var waits = new List<TimeSpan>();
        var keys = await Source(handler, delay: waits.Add).FindIssueKeysAsync(Window.From, Window.To, default);

        Assert.Equal(["DOUBLE-1"], keys);
        Assert.Equal([TimeSpan.FromSeconds(7)], waits);
    }

    [Fact]
    public async Task FallsBackToTheConfiguredWaitWhenTheHeaderIsAbsent()
    {
        // A throttle with no header at all. The fallback is a guess, and it is named as one
        // in configuration rather than buried as a literal.
        var handler = new ScriptedHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            SearchPage(total: 1, startAt: 0, keys: ["DOUBLE-1"]));

        var waits = new List<TimeSpan>();
        await Source(handler, waits.Add, fallbackRetryAfterSeconds: 11)
            .FindIssueKeysAsync(Window.From, Window.To, default);

        Assert.Equal([TimeSpan.FromSeconds(11)], waits);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfRetries()
    {
        var handler = new ScriptedHandler(_ => Throttled(retryAfterSeconds: 1));

        var source = Source(handler, _ => { }, maxRetries: 2);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => source.FindIssueKeysAsync(Window.From, Window.To, default));

        // The first attempt plus two retries.
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task ReadsOnlyStatusChangesOutOfTheChangelog()
    {
        // One edit that moved the status and reassigned the issue at the same time. The
        // assignee is a person's name and must not survive the call.
        var handler = new ScriptedHandler(Changelog(total: 1, startAt: 0, entries: """
            {
              "created": "2026-08-03T09:15:00.000+0000",
              "items": [
                { "field": "assignee", "fromString": "A Person", "toString": "Another Person" },
                { "field": "status", "fromString": "To Do", "toString": "In Review" }
              ]
            }
            """));

        var transitions = await Source(handler).GetStatusTransitionsAsync("DOUBLE-1", default);

        var only = Assert.Single(transitions);
        Assert.Equal("To Do", only.FromStatus);
        Assert.Equal("In Review", only.ToStatus);
    }

    [Fact]
    public async Task ReturnsTransitionsOldestFirstAcrossPages()
    {
        var handler = new ScriptedHandler(
            Changelog(total: 2, startAt: 0, entries: Entry("2026-08-05T16:00:00.000+0000", "In Review", "Done")),
            Changelog(total: 2, startAt: 1, entries: Entry("2026-08-03T09:00:00.000+0000", "To Do", "In Review")));

        var transitions = await Source(handler).GetStatusTransitionsAsync("DOUBLE-1", default);

        Assert.Equal(2, transitions.Count);
        Assert.Equal("To Do", transitions[0].FromStatus);
        Assert.Equal("Done", transitions[1].ToStatus);
    }

    [Fact]
    public async Task TimestampsComeBackInUtcWhateverOffsetTheTrackerUsed()
    {
        // The tracker answers in the instance's own offset. Anything that reaches the
        // measurement has to be an instant, not a local reading.
        var handler = new ScriptedHandler(
            Changelog(total: 1, startAt: 0, entries: Entry("2026-08-03T11:00:00.000+0200", "To Do", "In Review")));

        var transitions = await Source(handler).GetStatusTransitionsAsync("DOUBLE-1", default);

        Assert.Equal(TimeSpan.Zero, transitions[0].AtUtc.Offset);
        Assert.Equal(new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero), transitions[0].AtUtc);
    }

    [Fact]
    public async Task TheSearchWindowIsHalfOpenAndOrdered()
    {
        var handler = new ScriptedHandler(SearchPage(total: 0, startAt: 0, keys: []));

        await Source(handler).FindIssueKeysAsync(Window.From, Window.To, default);

        var query = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.Query);
        Assert.Contains("updated >= \"2026-08-01 00:00\"", query);
        Assert.Contains("updated < \"2026-09-01 00:00\"", query);
        Assert.Contains("ORDER BY updated ASC", query);
    }

    // ---- scaffolding -------------------------------------------------------------------

    private static class Window
    {
        public static readonly DateTimeOffset From = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        public static readonly DateTimeOffset To = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static JiraIssueSource Source(
        ScriptedHandler handler,
        Action<TimeSpan>? delay = null,
        int maxPages = JiraOptions.DefaultMaxPages,
        int maxRetries = JiraOptions.DefaultMaxRetries,
        int fallbackRetryAfterSeconds = JiraOptions.DefaultFallbackRetryAfterSeconds)
    {
        var options = new JiraOptions
        {
            Jql = "project = DOUBLE AND status WAS \"In Review\"",
            MaxPages = maxPages,
            MaxRetries = maxRetries,
            FallbackRetryAfterSeconds = fallbackRetryAfterSeconds,
        };

        return new JiraIssueSource(
            new HttpClient(handler) { BaseAddress = new Uri("https://tracker.invalid/") },
            Options.Create(options),
            NullLogger<JiraIssueSource>.Instance,
            (duration, _) =>
            {
                delay?.Invoke(duration);
                return Task.CompletedTask;
            });
    }

    private static HttpResponseMessage SearchPage(int total, int startAt, string[] keys)
    {
        var issues = string.Join(",", keys.Select(k => $$"""{"key":"{{k}}"}"""));
        return Json($$"""
            {"startAt":{{startAt}},"maxResults":50,"total":{{total}},"issues":[{{issues}}]}
            """);
    }

    private static HttpResponseMessage Changelog(int total, int startAt, string entries) =>
        Json($$"""
            {"startAt":{{startAt}},"maxResults":50,"total":{{total}},"values":[{{entries}}]}
            """);

    private static string Entry(string created, string from, string to) => $$"""
        {"created":"{{created}}","items":[{"field":"status","fromString":"{{from}}","toString":"{{to}}"}]}
        """;

    private static HttpResponseMessage Throttled(int retryAfterSeconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.Add("Retry-After", retryAfterSeconds.ToString());
        return response;
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    /// <summary>
    /// Answers from a script: a queue of prepared responses, or one factory used for every
    /// request when the test needs an endless supply.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage>? _queue;
        private readonly Func<int, HttpResponseMessage>? _always;

        public ScriptedHandler(params HttpResponseMessage[] responses) =>
            _queue = new Queue<HttpResponseMessage>(responses);

        public ScriptedHandler(Func<int, HttpResponseMessage> always) => _always = always;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            if (_always is not null)
            {
                return Task.FromResult(_always(Requests.Count));
            }

            if (_queue!.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The client asked for more than the script provides: request {Requests.Count} to {request.RequestUri}.");
            }

            return Task.FromResult(_queue.Dequeue());
        }
    }
}
