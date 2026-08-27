# sla-watcher: the decisions taken before the first line of code

A pet project with two goals, in this order: **find out whether the audit catches anything
real** after the intake, and get hands on Quartz with MongoDB on .NET along the way.

The service reads a tracker on a schedule, measures how long tickets have sat in a status in
the reviewer's working hours, and raises an escalation when one leaves the band.

**There is no AI inside it.** The service is deterministic. The model works from outside: an
agent writes the code under the rules the intake produces. Otherwise every "why did this job
fire twice" would start by ruling out the model, and a test of the template would turn into
debugging non-determinism.

**Jira is a local double in compose, not a live instance.** Not for convenience: everything
interesting here is behaviour under failure, and a real Jira will not return 429 on request or
truncate a changelog when you need it to. An untried 429 handler does not count as working,
which is the same canary rule the tests are held to.

The double serves the two endpoints the service actually calls (JQL search and the changelog
for one issue) and carries a control endpoint: return 429 with `Retry-After`, truncate a
changelog the way the real one does when `changelog.total > maxResults`, return two issues
sharing an `updated` value to the second. Integration tests run without an account, without
secrets and without network access.

⚠️ **The price, named in advance:** the double encodes my model of Jira's semantics. If that
model is wrong the double agrees with me, the tests go green, and the real instance breaks.
The cure is **one contract test against the real instance**, separately and later. Until then
every behaviour of the double is an assumption rather than a fact.

⚠️ The absence of a tracker here is a property of **a local experiment**. No company project
runs without Jira, and this part of the setup transfers nowhere.

---

## Decisions forced by the choice of stack

This is what seeds the intake: path C ("genuinely new, no precedent") in the protocol requires
starting from decisions rather than from incidents, because there are no incidents yet.

### 1. Misfire: do not catch up, wait for the next fire

`WithMisfireHandlingInstructionDoNothing` on the cron trigger.

A missed run heals itself here: the watermark has not moved, so the next run simply takes a
wider window. Catching up on every missed fire means reading the same window N times over and
sending the same escalations round again.

### 2. The watermark lives in Mongo, not in trigger state

One document per query: `lastSeenUpdated`. The JQL is
`updated >= lastSeen ORDER BY updated ASC`, in pages. The watermark advances **only after a
page has been processed in full**.

A consequence to accept deliberately: processing is **at-least-once**, so everything
downstream has to be idempotent. On top of that, `updated` in Jira has minute granularity, so
the window overlaps a minute backwards and relies on deduplication rather than risking a
miss.

### 3. An idempotency key on every escalation

`{issueKey}:{ruleId}:{thresholdCrossedAt}`, with a unique index in Mongo.

The order is **insert first, send second**. A crash between the two steps has to lose the
send rather than duplicate it. In that order two clustered instances that both picked up one
trigger are harmless: the second one trips over the unique index.

### 4. Backoff on 429, and no concurrent execution

`Retry-After` is honoured. `[DisallowConcurrentExecution]` on the job, plus a ceiling on
execution time: a throttled run must not overlap the next fire.

### 5. Status age comes from the changelog, not from `updated`

A separate call to `/rest/api/3/issue/{key}/changelog`.

`expand=changelog` on search **truncates the history** when `changelog.total > maxResults`,
already verified against live data. Without the changelog there is no way to know when a
ticket entered a status, which means there is no way to measure its age in that status at all.

### 6. Age is measured in the reviewer's working hours, not in calendar hours

This is the whole point of the metric. On real data the median in calendar hours doubled over
eight months, while in the reviewer's working hours it was flat: what grew was not the length
of review, it was the number of nights and weekends inside the interval.

**Mechanism, corrected 2026-08-26.** This decision used to name a Quartz calendar. It cannot
be one. `ICalendar` in Quartz 3.19.1 exposes `IsTimeIncluded` and `GetNextIncludedTimeUtc` and
nothing else, and both are questions about a single instant: a Quartz calendar can stop a
trigger firing out of hours, but it cannot say how much working time lies between two moments.
The measurement is our own code, `WorkingHours` over a `WorkingCalendar`. The intent of the
decision is unchanged; only the named mechanism was wrong.

### 7. `RequestsRecovery` is off

Decided 2026-08-23, after measuring on `2.2.0-rc.1`.

The slot an instance dies on is lost silently: recovery logs `1 abandoned` and the database
keeps a gap between adjacent minutes. Turning it on would hand that slot to a live instance.

**Why it stays off anyway.** A missed SLA reading is invisible to the customer. A repeated
escalation is not. And a repeated execution under recovery is not hypothetical: a frozen
instance comes back and finishes its own copy, which was measured, so a replayed slot can run
twice.

**What changed, and why it does not change the decision.** The deterministic key derived from
the slot is already in place, so a second execution collides with it, gets error 11000 and
logs a warning instead of writing a second record. The cost of turning recovery on has fallen
from "a duplicate escalation" to "an extra execution, caught and visible in the log". But the
key protects the **record**, not the side effect: while there is no real sending there is
nothing to protect, and once there is, the insert-then-send order has to be followed to the
letter, or the key saves the document and not the message.

**When to revisit.** Together with real sending, in the same decision as the order of the
insert relative to it. There is nothing to discuss before then.

---

## What is NOT in the first slice

One job, one trigger, one collection, one rule, escalation to the log. No dashboard, no
notifications, no second stack. The second stack (a small TypeScript dashboard) is added
**deliberately later**, to check that the intake writes an addon for a new stack and that
`check` fails honestly on its absence until then.

## The measure of success

Not a working service. There is exactly one question: **after the intake and the first slice
of code, did the audit find a single real problem**, or did it report clean on code that had
one.

If it reports clean, the second question becomes interesting: were the rules bad, or were
there genuinely no problems. Only a deliberately introduced fault from the list above answers
it: remove `insert-then-send` and see whether the audit says anything at all.
