---
created: 2026-08-23
updated: 2026-08-23
type: reference
status: active
area: scheduler
tags: [quartz, mongodb, clustering, handover]
---

# Brief for the watcher: taking the store fix

The counterpart to `store-fix-brief.md`. That one said what the job store had to do; this one says
what this service has to do once it has it. Evidence for all of it is in
`pod-death-and-the-job-store.md`.

## What changed on the store side

`cotique.Quartz.Spi.MongoDbJobStore` now keeps a check-in and reclaims the work of an instance that
stops answering. Branch `feat/cluster-recovery`, commit `615e361`, not released yet. The published
`2.1.0-rc.1` this service currently pins does **not** have it.

What it does: every `clusterCheckinInterval` (15 seconds by default) each instance stamps its row in
`schedulers` and looks at everyone else's. An instance whose stamp is older than the failure window
gets its `firedTriggers` records released or rescheduled and then deleted, and its row removed, all
under the `TriggerAccess` lock. Measured on two containers: OOM kill at 13:39:31, the survivor
picked up the next slot at 13:40:00 with no restart, orphan gone.

What it does not do: stop a job that is already running in another process. A frozen instance that
comes back still finishes what it was doing. The store refuses to fire a trigger whose claim was
taken and refuses to write trigger state for an execution it no longer owns, and it logs both, but
the job's own side effect is out of its reach. That part stays this service's problem.

## Work items

### 1. Idempotency key derived from the slot

`.claude/audit-rules/dotnet.md` already requires this and `FireLog` does not do it.
`FireLog.RecordAsync` builds a document with no `_id` and calls `InsertOneAsync`, and there is no
unique index on the collection. So a retry after an ambiguous write, or the thawed-pod case, writes
a second row instead of failing on a duplicate key. The rule was written after the ambiguous write
was observed; the code was not changed to match.

Derive `_id` from `scheduledFireTimeUtc` (the slot), not from `fireInstanceId` (the attempt), and
put a unique index behind it. Then a duplicate execution is a `MongoWriteException` with code 11000,
which is a fact you can act on, rather than a second document nobody notices.

This one is worth doing **first and on its own**, before any upgrade. It is the only thing here that
protects against a double execution, and right now nothing does.

### 2. Cold start of several instances loses one

Two pods starting at once against an empty database race to create the job, and the loser dies:

```
Quartz.ObjectAlreadyExistsException: Unable to store Job: 'DEFAULT.tick', because one already
exists with this identification.
```

Reproduced three times out of three. Not a store defect: `IJobStore.StoreJobAndTrigger` has no
"replace" parameter and is defined to refuse duplicates. The race is in Quartz's declarative
initialisation, which checks whether the job exists and then calls `ScheduleJob(job, trigger)` for
the ones that did not. `OverWriteExistingData` does not help, it is already `true` by default and
only covers a duplicate that is visible at the moment of the check.

Fix: take the schedule out of `AddQuartz` and install it after the scheduler starts, from an
`IHostedService` registered **after** `AddQuartzHostedService` (hosted services start in
registration order):

```csharp
await scheduler.AddJob(job, replace: true, storeNonDurableWhileAwaitingScheduling: true, ct);

if (await scheduler.CheckExists(trigger.Key, ct))
    await scheduler.RescheduleJob(trigger.Key, trigger, ct);
else
    await scheduler.ScheduleJob(trigger, ct);
```

Wrap it in a short retry catching `ObjectAlreadyExistsException`: the trigger half still has a
window, and on the retry the other instance's write is visible so the `RescheduleJob` branch takes
it. `AddJob` with `replace: true` is an upsert in the fixed store, so that half needs no retry.

A working version is in `docs/attachments/ScheduleInstaller.cs`, verified three cold starts out of
three with both pods started simultaneously against an empty database. Note it depends on the store
fix for the upsert; on `2.1.0-rc.1` the `AddJob` half can still race into a driver-level duplicate
key error.

The cheap alternative, if this is not worth code right now: `restart: unless-stopped` in compose.
The loser restarts, the job exists by then, and the second attempt is clean. It works, at the cost
of a crash entry in the logs on every cold start.

### 3. Upgrade, and do not overlap versions

`2.1.0-rc.1` and everything before it never check in, so their `schedulers` row keeps whatever stamp
it got at startup. A new instance reads that, finds it minutes old, and reclaims work the old
instance is still doing. **Take the old pods down before bringing new ones up.** A rolling update
across this version boundary will double-execute, which is exactly the thing the whole exercise was
meant to prevent.

### 4. Decide on `RequestsRecovery`

It is currently off (the default), so the slot an instance died on is simply lost: recovery logs it
as `abandoned` and moves on. With the fix there is now a real choice.

- Off: a killed pod loses one slot. The next slot fires normally.
- On: the interrupted slot is rescheduled onto a live instance. But if the instance was frozen
  rather than dead, it comes back and finishes its copy, so that slot runs twice.

Only worth turning on once item 1 is done, and even then it is a judgment call about whether a
missed slot or a repeated one costs more here. Write down which and why, the same way the
`DisallowConcurrentExecution` decision was written down.

### 5. Rules and docs that are now wrong

Both of these currently state the failure as permanent, which stops being true after the upgrade.
Update them in the same PR, not later.

- `.claude/audit-rules/dotnet.md`, the `[DisallowConcurrentExecution]` rule: "with `Clustered =
  false` no recovery scan ever reclaims it" and "would have sat there forever" become false. The
  attribute still is not free, but the cost drops from a permanently stopped schedule to a gap of
  roughly the failure window, measured at 33 seconds. Keep the rule, restate the cost.
- `pod-death-and-the-job-store.md`: mark it as describing `2.1.0-rc.1` behaviour and link forward,
  rather than leaving it reading as current. Its "Not established" list also has an item that now
  has an answer: a frozen instance is reclaimed and the thawed one does not corrupt trigger state.

## Acceptance, and the order matters

Same rule as last time: reproduce before fixing, on the current code, by eye. Two containers
throughout, because none of this is visible on one.

**Item 1.** Insert the same slot twice by hand, before the change: two documents. After: a
duplicate key error on the second. Then the real path, a job that throws after its write, and check
that a retry does not add a row.

**Item 2.** `docker compose up` both pods against a dropped database, three times. Before the fix
one pod exits on at least one of the three. After: both alive all three times, one job, one trigger,
two scheduler rows.

**Item 3.** Not a test, a deployment note. Put it where the deploy procedure lives.

**Item 4.** If it goes on: kill mid-execution and confirm the slot is rerun. Then freeze, hold past
the failure window, thaw, and count executions of the frozen slot. Two is the expected outcome here
and the reason item 1 comes first.

## Out of scope

Do not change the job store from this repository. If something in the store looks wrong, it goes to
`cotique/mongodb-quartz-net` with a reproduction, the way this one did.

Do not remove `[DisallowConcurrentExecution]`. That decision is recorded in
`pod-death-and-the-job-store.md` as an accepted cost: two workers reading one watermark would
duplicate escalations, which is what this service exists to avoid.
