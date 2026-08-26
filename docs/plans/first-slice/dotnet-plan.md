---
created: 2026-08-26
updated: 2026-08-26
type: plan
status: draft
area: jira
tags: [sla, quartz, mongodb, working-hours, first-slice]
---

# First slice: collect, compute, publish a month

One monthly job that reads a closed month out of the tracker, computes the age of tickets in
a status in the reviewer's working hours, writes one aggregate document, and logs the
breaches. **Nothing from the tracker is stored.**

The number is fetched by a person on the first working day of the month, so the job's whole
obligation is that the previous month's document exists by then and does not move afterwards.

---

## Fact sheet

Everything below was checked in this repository or by reflection against the referenced
packages on 2026-08-26. Nothing here is from memory.

### Quartz calendars cannot measure an interval

`Quartz.ICalendar`, Quartz 3.19.1, has exactly two behavioural members:

```
bool           IsTimeIncluded(DateTimeOffset)
DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset)
```

Both are point queries. There is no member that answers "how much included time lies between
A and B". `DailyCalendar` carries `InvertTimeRange`, `RangeStartingTime`, `RangeEndingTime`,
`TimeZone`; `WeeklyCalendar` carries `DaysExcluded`, `TimeZone`; both chain through
`CalendarBase`.

**Consequence:** a Quartz calendar can stop a trigger firing out of hours. It cannot measure
working hours. The measurement is our own code. This restates decision 6, whose intent stands
and whose named mechanism does not.

### What exists in the code today

| File | What it gives this slice |
|---|---|
| `src/SlaWatcher/SchedulerOptions.cs` | `ReadAndValidate(IConfiguration)`, validated before any value is used. New settings go here and are validated by the same call. |
| `src/SlaWatcher/FireLog.cs` | The pattern this slice copies: `SlotKey` derives a deterministic `_id`, insert collides on 11000 rather than duplicating. |
| `src/SlaWatcher/ScheduleInstaller.cs` | Installs job and trigger after the scheduler is up, idempotent, retries `ObjectAlreadyExistsException` three times. The new trigger is installed here. |
| `src/SlaWatcher/TickJob.cs` | The skeleton heartbeat. Retired at the end of this slice, see "Renaming is a migration". |
| `src/SlaWatcher/Program.cs` | Composition. Quartz properties are plain strings assembled at build time. |

Collections present in the database now: `fires`, `quartz.schedulers`, `quartz.triggers`,
`quartz.firedTriggers`, `quartz.jobs`, `quartz.locks`.

### Measured behaviour this slice must live with

- An instance that dies, freezes or loses the network costs **one slot**, not the schedule
  (store 2.2.0, three scenarios, `docs/pod-death-and-the-job-store.md`).
- `RequestsRecovery` is off, decision 7. A lost slot is not replayed.
- A write that reports failure may already have been applied. The key is derived from the
  work, never from the attempt.
- **The store protects the record, not the work.** Measured on the network cut: the execution
  ran to completion and only its write failed. Anything with a side effect outside Mongo has
  to order itself insert-then-send.
- Cron below one minute is silently eaten by Quartz's idle wait. Irrelevant at monthly
  cadence, recorded so nobody re-derives it.

### Recorded in DECISIONS.md, taken as given

- Two endpoints: `/rest/api/3/search` for JQL, `/rest/api/3/issue/{key}/changelog` per issue.
- `expand=changelog` on search **truncates** history when `changelog.total > maxResults`.
  Verified on live data by the author. This is why the changelog is a separate call per issue.
- Misfire instruction is stated explicitly on every trigger.
- 429 honours `Retry-After`.

### NOT facts, and the plan must not pretend otherwise

The exact JSON of a changelog page — field names, the shape of a status transition, paging
keys — **is not verified here and is not in this repository.** I have no access to the
instance and will not write field names from memory into a plan.

They are handled as a contract: the port below declares what the code needs, the double
implements exactly that, and a single contract test against the real instance promotes the
contract from assumption to fact. Until that test runs, every number produced from the double
is a number about the double.

---

## What this slice is, and is not

**Is:** one monthly job, one trigger, two collections, one rule, breaches to the log.

**Is not:** no real sending, no dashboard, no second stack, no AI. No storage of any ticket
field.

---

## Decisions this slice changes

Each is rewritten in `DECISIONS.md` in the same change, with the new reason, never deleted.

### Decision 2 is reversed: there is no watermark

It existed for incremental reading. With a monthly run over a closed month there is nothing
to avoid re-reading: the run happens once and the window is fixed. Removing it removes
at-least-once semantics, the minute-granularity overlap on `updated`, and the "advance only
after a full page" rule along with it.

**The reason it is worth reversing, and it is not tidiness.** The raw data is not kept, by
rule. So with a watermark, a month's number is frozen at whatever the code of that month
produced — its threshold, its calendar, its bugs. Recomputation is what lets a changed metric
definition be applied backwards, and that has already been needed once in real work: review
latency looked like it doubled in calendar hours and was flat in the reviewer's working
hours, and telling those apart required recomputing history under the new definition.

**Cost, named:** a recomputed month depends on the tracker's current state. Tickets edited
after the fact make a recomputed July differ from the July already quoted.

### New: the published month is insert-only

`_id` is the month. First computation wins and is never overwritten, so a number that has
been quoted does not move. A recomputation is written beside it with its own timestamp, and
the difference between the two is itself a finding.

### Decision 6 keeps its intent and changes its mechanism

Working hours, not calendar hours. Not via a Quartz calendar, see the fact sheet.

### Decision 3 is deferred, not dropped

Escalations are log lines here. The idempotency document and the insert-then-send order
arrive together with a real send, which is also when decision 7 gets revisited. Recorded so
the absence is a decision and not an oversight.

### New: misfire policy needs the job to be month-driven

`WithMisfireHandlingInstructionDoNothing` was safe when a missed fire meant the next run took
a wider window. At monthly cadence it is not: a run missed on 1 November would be followed by
one on 1 December, and if the job computes "last month" then October is computed by nobody.

**So the job does not compute "last month". It computes every closed month that has no
published document**, from a configured first month forward. A missed fire heals on the next
one, a manual run backfills, and the reason behind decision 1 survives in the new shape.

---

## Data model

Two new collections. Nothing else is written, and nothing from the tracker is stored.

### `monthlyStats` — the published number, insert-only

```
_id                    "2026-07"                      the month it describes
computedAtUtc          ISODate
statusName             "In Review"                    which status was measured
thresholdWorkingHours  16                             the band in force when it was computed
issuesConsidered       142
medianWorkingHours     11.5
p90WorkingHours        38.0
overThreshold          19
calendarVersion        "2026-08-26"                   which working calendar produced it
```

`statusName`, `thresholdWorkingHours` and `calendarVersion` are stored **so the number can be
read a year later**. A metric without the definition that produced it is not a metric.

Insert only. A duplicate key (11000) means the month is already published, which is the
answer, not an error.

### `monthlyStatRecomputes` — everything after the first

```
_id     "2026-07:2026-09-14T08:00:00.0000000Z"   month + computedAtUtc, round-trip format
        ... the same fields ...
```

Same derivation rule as `FireLog.SlotKey`: the key comes from the work and the moment, never
from the attempt, so a retry after an ambiguous write collides instead of adding a row.

---

## The tracker port

One interface, so the double and the real instance are interchangeable and the job never
sees an HTTP concept.

```csharp
// NEW — src/SlaWatcher/Jira/IIssueSource.cs
public interface IIssueSource
{
    /// Issues that left the measured status inside [fromUtc, toUtc).
    Task<IReadOnlyList<string>> FindIssueKeysAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);

    /// Every transition into and out of the measured status, oldest first.
    Task<IReadOnlyList<StatusTransition>> GetStatusTransitionsAsync(
        string issueKey, CancellationToken cancellationToken);
}

// NEW — src/SlaWatcher/Jira/StatusTransition.cs
public sealed record StatusTransition(DateTimeOffset AtUtc, string FromStatus, string ToStatus);
```

`FindIssueKeysAsync` returns keys only. No summary, no description, no assignee — the fields
that must never be stored are never even materialised, which is a stronger guarantee than
remembering not to log them.

Two implementations: `JiraIssueSource` (HTTP, `IHttpClientFactory`, `Retry-After` honoured,
paging with a hard iteration cap) and the double in `tools/jira-double/`.

---

## The measurement

```csharp
// NEW — src/SlaWatcher/WorkingHours.cs
public sealed class WorkingHours
{
    public WorkingHours(WorkingCalendar calendar);

    /// Working time between two instants, zero when to <= from.
    public TimeSpan Between(DateTimeOffset fromUtc, DateTimeOffset toUtc);
}

// NEW — src/SlaWatcher/WorkingCalendar.cs
public sealed record WorkingCalendar(
    TimeZoneInfo TimeZone,
    TimeOnly DayStart,
    TimeOnly DayEnd,
    IReadOnlySet<DayOfWeek> WorkingDays,
    IReadOnlySet<DateOnly> Holidays,
    string Version);
```

Pure, no infrastructure, no Quartz. This is the only real arithmetic in the service and the
one place a wrong answer is invisible, so it carries the heaviest test set.

---

## The job

```csharp
// NEW — src/SlaWatcher/MonthlyStatsJob.cs
[DisallowConcurrentExecution]
public sealed class MonthlyStatsJob : IJob
```

Order inside one execution:

1. Ask `MonthlyStatsStore` which closed months between the configured first month and the
   previous month have no published document.
2. If none, log and return. This is the normal case eleven runs out of twelve.
3. For each missing month, oldest first:
   a. `FindIssueKeysAsync` over the month.
   b. Per key, `GetStatusTransitionsAsync`, pair the transitions, measure each interval with
      `WorkingHours.Between`.
   c. Aggregate.
   d. Publish. A duplicate key means another instance got there first: log and move on.
   e. Log each breach, one line per issue key, key and hours only.
4. A month that throws does not stop the others, and its failure is logged at error.

Runtime cap from configuration, so a throttled run cannot overlap the next fire — the trigger
is monthly, so the cap is about not holding a lock for a day, not about overlap.

---

## Configuration

Added to `SchedulerOptions`, validated by the existing `ReadAndValidate`:

```
Scheduler:MonthlyCron            "0 0 3 1 * ?"     03:00 on the 1st
Scheduler:FirstMonth             "2026-01"         backfill floor
Scheduler:MeasuredStatus         "In Review"
Scheduler:ThresholdWorkingHours  16
Scheduler:MaxMonthsPerRun        3                 a bounded backfill, not a stampede
Jira:BaseUrl                     local config only
Jira:Token                       user-secrets or environment, never a file in the repo
```

---

## Renaming is a migration

Quartz persists the CLR type name. Retiring `TickJob` orphans its stored trigger, which does
not error — it stops firing. So the last stage removes the `tick` job and trigger through the
scheduler API in `ScheduleInstaller`, in the same change that stops registering the class.

---

## Stages

Each is one session and lands on its own branch through a pull request.

| # | Deliverable | Depends on |
|---|---|---|
| 1 | `WorkingCalendar`, `WorkingHours`, its test set | nothing |
| 2 | `IIssueSource`, `StatusTransition`, the double in `tools/jira-double/` plus compose | nothing |
| 3 | `MonthlyStatsStore`, both collections, integration tests | 1 |
| 4 | `MonthlyStatsJob`, wiring, trigger install, `TickJob` retired | 1, 2, 3 |
| 5 | One contract test against the real instance, run locally | 2 |

Stage 5 is what turns the tracker contract from assumption into fact. Until it runs, the
service is verified against my model of the tracker.

---

## Test plan

**Working hours, unit, no infrastructure.** An interval inside one working day. One spanning a
night. One spanning a weekend. One over a holiday. One starting and ending outside hours. A
zero interval and a reversed one. A timezone with a DST change inside the interval — the case
that makes a naive implementation wrong twice a year.

**Aggregation, unit.** Empty month. One issue. An issue that entered the status and never
left. An issue that entered twice. Median and p90 on an even and an odd count.

**Store, integration, `[Trait("Category", "Integration")]`.** Publishing twice collides on
11000 and the first document is unchanged. A recompute writes beside it. Missing-month
detection with a gap in the middle.

**The double, integration.** 429 with `Retry-After` is honoured, driven through the control
endpoint. A truncated changelog is paged to the end. Two issues with the same `updated` to
the second are both returned.

Every one of these is watched failing before it is accepted, per the canary rule.

---

## Self-audit

```
[x] Data references: both collections and every field listed with its purpose
[x] Method calls: existing ones cited from the files; every new one carries a full signature
    and is marked NEW
[x] New methods: each has happy, error and edge cases in the test plan
[x] Cross-references: the stage table covers every component named above, and every
    component named above appears in a stage
[x] Decisions: every DECISIONS.md entry this contradicts is listed with its replacement
[x] Unverified claims are marked unverified rather than written as facts
```

## Open, and deliberately not assumed

- **The contents of the monthly document** are my proposal. The fields chosen are the ones
  that make the number readable a year later; the statistics themselves may be the wrong
  ones.
- **The measured status** is written as a single status. If the band applies to several, the
  aggregate grows a dimension and the document shape changes.
- **Deployment** has no decision at all, which is recorded separately and does not block this
  slice.
