---
created: 2026-08-23
updated: 2026-08-23
type: reference
status: active
area: scheduler
tags: [quartz, mongodb, clustering, handover]
---

# Brief for the job store: recovery after an instance stops answering

For whoever works on `cotique.Quartz.Spi.MongoDbJobStore`. Evidence and timelines are in
`pod-death-and-the-job-store.md` next to this file; this is the part that says what to do.

## What is broken

One instance that stops answering — killed, OOMed, frozen, paused, stalled — stops the
schedule for everyone, permanently.

Sequence, reproduced twice by two different means:

1. The instance is executing. A `firedTriggers` document exists in state `Executing`.
2. The instance stops. Nothing removes that document: the process that would have is gone or
   frozen.
3. The job carries `[DisallowConcurrentExecution]`, so the store holds the trigger `Blocked`.
4. The store reports `Clustered = false`, so no recovery scan ever runs. `LastCheckIn` was
   stale for the **live** instance too — nobody maintains liveness at all.
5. Surviving instances have no path to that trigger. Measured: idle for four and a half
   minutes on a kill, three and a half on a freeze, and would have stayed there.

A restart of any instance clears the `Blocked` state but leaves the orphaned document
behind, and they accumulate.

## Why it is the store's problem and not Quartz's

In Quartz.NET cluster recovery is implemented **inside the job store**, not in the core:
`ClusterManager.cs` and the check-in logic live in `src/Quartz/Impl/AdoJobStore/`. The ADO
store does it for itself. A Mongo store gets nothing for free and has to drive its own loop.

## What already exists

- `Models/Scheduler.cs` — the `LastCheckIn` field.
- `MongoDbJobStore.cs:149` — `LastCheckIn` is written **once**, when the scheduler row is
  created, and never updated again.
- `MisfireHandler.cs` — a background loop already runs; periodic work has somewhere to live.
- No scan for failed instances anywhere.

## First, one word

`MongoDbJobStore.cs:149` uses `DateTime.Now`. Local time in a distributed store is wrong:
two instances in different zones compare each other's check-ins with an hours-long error, and
every "older than N seconds" threshold built on top of it lies. Change it to `UtcNow` before
anything else, and check whether `Now` appears elsewhere.

## What to build

1. **Periodic check-in.** Update this instance's `LastCheckIn` from the existing background
   loop. Interval configurable; the ADO store's equivalent is `clusterCheckinInterval` at 15
   seconds.
2. **Failure scan.** Instances whose check-in is older than a threshold are treated as
   failed. The threshold is not the interval — it has to survive a GC pause and database lag.
3. **Recovery.** For each failed instance, take its `firedTriggers`; per document, return the
   trigger to `Waiting` or reschedule it when the job requests recovery; then delete the
   document and the scheduler row.
4. **Under the `TriggerAccess` lock.** Otherwise two live instances start recovering the same
   dead one at once.

Mirror for reference: `ClusterManager.cs` and `JobStoreSupport.cs` in quartznet. Do not copy
blindly — that code has transactions, this store does not, and that changes the order of
operations.

## The part that is easy to get wrong

**A stalled instance is indistinguishable from a dead one, and it comes back.**

Measured: a paused pod looked exactly like a killed one for three and a half minutes, and
when it was unpaused it *finished the slot it had been frozen on* and wrote its record.

So a recovery that reassigns work on a staleness threshold will produce two executions of one
slot — precisely what `DisallowConcurrentExecution` was there to prevent. Turning a stopped
schedule into a duplicated one is not a fix.

The instance that comes back has to confirm its claim is still its own before it commits
anything. Whatever the mechanism — a fencing token on the fired-trigger document, a
compare-and-set on recovery, an ownership check before the final write — it has to exist, and
the test below has to prove it.

## Acceptance, and the order matters

**Reproduce the failure on the current code first, by eye.** A green run on unfixed code
proves nothing; that is the same canary rule as everywhere else.

Two instances against one database throughout. On one instance this bug is invisible.

**Scenario 1 — the instance dies.** `mem_limit` on the container, a job that takes native
memory and does not survive the limit, `[DisallowConcurrentExecution]` on the job. Kill it
mid-execution.

- the surviving instance picks up the next slot without a manual restart
- the orphaned document is gone
- no `firedTriggers` document in `Executing` outlives a plausible execution

**Scenario 2 — the instance freezes and comes back.** `docker pause` the executing
instance, hold it past the recovery threshold, then `docker unpause`.

- the surviving instance picks up the work, as above
- **and the thawed instance does not complete it.** One slot, one execution, whichever side
  wins. This is the criterion that separates a fix from a different bug.

**Scenario 3, cheap and worth it.** A write that reports failure may have been applied: a
thawed insert threw `MongoConnectionException` / `SocketException (125)`, Quartz logged the
job as failed, and the document was in the collection. If recovery re-drives anything that
writes, the same ambiguity applies to it.

## Out of scope

Do not change single-instance behaviour. Do not try to set `Clustered = true`: the property
has no setter, Quartz throws at startup if you pass it, and flipping it is a separate
decision with its own consequences.
