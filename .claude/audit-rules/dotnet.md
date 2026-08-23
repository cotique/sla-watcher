# dotnet — audit addon

Loaded when the change touches `**/*.cs`, `*.csproj` or `*.sln`.

> Path C: no incidents yet, so every rule is `[predicted]`. When one catches a real defect,
> retag it `[observed]` and add the one-line story.
>
> `[mechanical]` — checkable without opinion: grep, a type check, a config read. The audit
> may fail the review on it. `[judgment]` — needs a human to weigh context. The audit raises
> it and says why; it never fails a review on judgment alone.

## Quartz (section 2)

- [predicted] [mechanical] **Job type names are persistent data.** Quartz stores the CLR type name in
  Mongo. Renaming or moving a job class orphans its stored triggers, which then silently
  never fire. A rename needs a migration that rewrites the stored names, in the same PR.
- [predicted] [mechanical] **Every trigger states its misfire instruction explicitly.** An unstated default
  is a decision nobody made.
- [observed] [mechanical] **Cron intervals are one minute or longer.** Quartz is steady from a
  minute up and has never worked properly below it: the scheduler paces on
  `quartz.scheduler.idleWaitTime`, thirty seconds by default, so a five-second cron fires
  roughly twice a minute and the skipped slots are silently eaten by the misfire policy.
  Measured here: a 5s cron produced 2 fires in 35 seconds, one of them 25.7s late, while the
  trigger sat in `Waiting` with its fire time already past. Dropping idleWaitTime to a second
  fixes the cadence and is not a trade worth making. **This is Quartz, not the job store** —
  it looked like a store defect for an hour, and the store's acquisition query does honour the
  requested window.
- [observed] [judgment] **`[DisallowConcurrentExecution]` is not free, but the cost is now a
  gap and not a stop.** A pod killed mid-execution leaves a `firedTriggers` document in state
  `Executing` and the store holds the trigger `Blocked` against it. On `2.1.0-rc.1` nothing
  ever reclaimed that: measured 2026-08-23, the live pod sat idle for four and a half minutes
  and would have stayed there until someone restarted something. From `2.2.0` the store
  keeps a check-in and reclaims the work of an instance that stops answering: measured on the
  same bench, kill at 16:14:25, the slot being executed lost, and the next slot fired on time
  on the surviving pod with no restart. **One slot, not the schedule.**
  Keep it where an overlap would corrupt state — the watermark is that case, and two workers
  reading one tracker would duplicate escalations. Full account in
  `docs/pod-death-and-the-job-store.md`.
- [observed] [mechanical] **Work is idempotent and recorded before the side effect.** Follows
  from the above: the process can vanish between any two lines, and the system has to survive
  that, not just the pod.
- [observed] [mechanical] **A write that reports failure may have been applied.** Measured
  2026-08-23: a frozen and thawed pod's insert threw `MongoConnectionException` /
  `SocketException (125)`, Quartz logged the job as failed, and the document was in the
  collection. So the idempotency key is **derived from the slot and deterministic** — a fresh
  identifier per attempt collides with nothing and a retry duplicates. And a failed write is
  re-read before it is re-attempted, never assumed absent.
- [observed] [judgment] **A stalled process is indistinguishable from a dead one**, so any
  recovery based on a staleness threshold will reassign work that is still in flight. The
  instance that comes back has to confirm its claim still belongs to it before committing.
- [predicted] [judgment] Jobs take dependencies through DI, not `JobDataMap`. The map is serialised into
  Mongo, so anything put there becomes a schema to migrate.
- [predicted] [mechanical] No job catches `Exception` and returns quietly. A swallowed failure looks like a
  successful run, and the next run advances past the window that failed.

## MongoDB (section 9)

- [predicted] [mechanical] **Uniqueness is an index, never a code check.** Check-then-insert is not atomic;
  two instances both pass the check.
- [predicted] [mechanical] Index creation is idempotent and runs at startup. There are no relational
  migrations here — index definitions are the schema and they live in code.
- [predicted] [judgment] Document shape changes carry a version field or a tolerant reader. Mongo stores
  both shapes happily and fails later, in a query.
- [predicted] [judgment] Filters on unindexed fields are flagged. On a collection that grows per ticket
  per run, a collection scan is an incident waiting for volume.

## HTTP client (sections 2 and 7)

- [predicted] [mechanical] `HttpClient` comes from `IHttpClientFactory`. A per-call `new HttpClient()`
  exhausts sockets; a static one never notices DNS changes.
- [predicted] [mechanical] Rate limiting honours `Retry-After`.
- [predicted] [mechanical] Every outbound call has a timeout. The practical default is infinite, and an
  infinite call inside a scheduled job holds its trigger forever.
- [predicted] [judgment] Paging loops have a hard iteration cap. A server that keeps returning a next
  page — the double included — turns an unbounded loop into a hang.

## Async (section 3)

- [predicted] [mechanical] No `.Result`, no `.Wait()`, no `async void` outside event handlers.
- [predicted] [mechanical] `CancellationToken` is accepted and passed down. A job that ignores cancellation
  cannot be stopped and shutdown waits for it.

## File layout (section 3)

- [predicted] [mechanical] **File-scoped `namespace` comes first, usings after it.** Not the
  order the templates generate, so it gets undone by anyone who lets the IDE reformat. House
  style, applied consistently rather than argued about per file.

## Configuration (section 4)

- [predicted] [mechanical] Settings bind to typed options validated at startup. A missing connection string
  fails at boot, not on the first fire at three in the morning.
- [predicted] [mechanical] No secret in `appsettings*.json`; the Jira token is read from user-secrets or
  the environment.
- [predicted] [mechanical] No port or host literal outside compose and configuration.
- [predicted] [mechanical] **`appsettings.json` carries no connection string.** The default
  belongs in `appsettings.Development.json`; the base file staying empty is what makes a
  non-Development run fail at boot rather than connect to whatever answers on the default
  port. Adding a "sensible default" there removes the only guard.
- [predicted] [judgment] **Schedules live apart from the rest of the settings once there is
  more than one job.** A `TickCron` next to a connection string is fine for one job and turns
  into a scattered set of unrelated cron strings at three. Move them to their own section,
  keyed by job, at the moment the second job appears — not later, when the shape is already
  copied.

## Logging (section 3)

- [predicted] [mechanical] `ILogger<T>` with message templates and named properties. No interpolation into
  the message — it destroys the template and any chance of querying by field.
- [predicted] [mechanical] The issue key may be logged. Summaries, descriptions, comments and people's
  names may not.
- [predicted] [mechanical] Every log line inside a job carries the fire instance id, or a run cannot be
  reconstructed afterwards.

## Tests (section 8)

- [predicted] [mechanical] xUnit. Anything touching the job store, Mongo or the double is an
  integration test and carries `[Trait("Category", "Integration")]`. Without the trait it runs
  in CI, where there is no database by design.
- [predicted] [mechanical] An integration test whose dependency is missing **fails**. It does
  not skip and it does not pass. `Skip` on a missing database turns a red suite green.
- [predicted] [mechanical] Missed fire, duplicate fire and 429 each have a test that causes the failure on
  purpose, through the double's control endpoint.
