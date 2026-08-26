---
created: 2026-08-23
updated: 2026-08-23
type: analysis
status: completed
area: scheduler
tags: [quartz, mongodb, clustering, failure-modes]
---

# What a dead pod does to the schedule

Run on 2026-08-23 against `cotique.Quartz.Spi.MongoDbJobStore` **2.1.0-rc.1**, Quartz 3.19.1,
MongoDB 8.2.12, two containers on one database.

> **This describes 2.1.0-rc.1 and is no longer current.** From `2.2.0` — first in `2.2.0-rc.1`,
> which is what the measurement below was taken on — the store keeps a
> check-in and reclaims the work of an instance that stops answering. Re-measured on the same
> bench after upgrading: kill at 16:14:25, the slot under execution lost, and the next slot
> fired on time on the surviving pod with no restart. The permanent stop below became a
> one-slot gap. What is still true, and is why this file is kept: the mechanism, the reason a
> frozen instance is indistinguishable from a dead one, and the ambiguous write.

**The result in one line: a pod killed by the kernel mid-execution stops the schedule, and
the surviving pod does not take over.**

## Why containers

A process on the host cannot be killed by the kernel at a moment it chooses. `Stop-Process`
kills at a moment *I* choose, which is not the same experiment: the interesting case is the
one where the process had no idea it was about to end. A container with `mem_limit` and a
job that takes native memory gives the kernel that decision.

Native, not managed, and written to page by page. A managed allocation is answered with
`OutOfMemoryException`, which the process survives, and surviving is the opposite of what
this needs. `DOTNET_gcServer=0` stops the runtime sizing its heap to the cgroup and refusing
first.

## What happened

| Time | Event |
|---|---|
| 12:44 | pod-a fires, completes, writes its record |
| 12:45 | pod-a fires, holds, allocates, **OOMKilled, exit 137** |
| 12:45–12:49 | pod-b alive and idle. **Nothing fires for four and a half minutes** |
| 12:49 | pod-b restarted by hand. Trigger returns to `Waiting` |
| 12:51 | pod-b fires, completes |
| 12:52 | pod-b fires, holds, allocates, **OOMKilled** |
| 12:59 | Trigger `Blocked` again, two orphaned records, nothing running |

State left in Mongo after each kill:

```
quartz.firedTriggers: { InstanceId: "pod-a", State: "Executing", Scheduled: 12:45:00 }
quartz.triggers:      { State: "Blocked", NextFireTime: 12:46:00 }   # in the past, frozen
```

## The mechanism

1. The kill leaves a `firedTriggers` document in state `Executing`. Nothing removes it: the
   process that would have is gone.
2. The job is `[DisallowConcurrentExecution]`, so the store sees an execution in flight and
   moves the trigger to `Blocked`.
3. The store reports `Clustered = false`. Cluster recovery — the scan that notices an
   instance stopped checking in and reclaims its work — never runs. `LastCheckIn` was stale
   by three minutes **for the live pod as well**, which is the tell: nobody is maintaining
   liveness at all.
4. So the surviving pod has no path to that trigger. It is not slow, it is stopped.

## What clears it, and what does not

- **A restart clears the block.** Restarting *the other* pod was enough: on startup the
  store recovers and the trigger returns to `Waiting`.
- **A restart does not remove the orphaned record.** After two kills there were two, and
  they stay. A pod crash-looping fills the collection with dead executions.
- **`restartPolicy` alone does not save the schedule.** It restores a pod, and a pod
  starting up does clear the block — but only when one actually restarts. Two live pods and
  one dead one is the stuck case, and that is the normal shape of a rolling update gone
  wrong.

## What follows for a deployment

- Anything scheduled here must assume its own death mid-execution is survivable by the
  system, not just by the pod. That means the work is idempotent and the record of it is
  written before the side effect, not after.
- `DisallowConcurrentExecution` is not free: it is what converts a dead pod into a stopped
  schedule. Here it stays on regardless — two workers reading one tracker would duplicate
  escalations, and that is the thing this service exists to avoid. The cost is accepted, not
  overlooked, and the mitigation is the monitoring below rather than removing the attribute.
- Watch `firedTriggers` for documents in `Executing` older than a job could plausibly run.
  That is the signal, and nothing else reports it: no error, no log line, no failed health
  check. The schedule simply stops.

## A frozen pod is indistinguishable from a dead one

`docker pause` on the pod that was executing, same setup, no allocation involved. The
process is alive, its TCP connections stay open, and not one instruction runs.

Three and a half minutes later: trigger `Blocked`, next fire time frozen in the past, the
`Executing` record still there, `fires` empty, and **the live pod had fired nothing**.
Identical to the kill.

So the failure is not "a pod died". It is "a pod stopped answering", and that includes a
long GC pause, a suspended VM, a stalled disk, a frozen node. The store cannot tell those
apart from death, because nothing maintains liveness in the first place.

**Then it thawed and finished its work.** The slot it was frozen on completed and wrote its
record. Which means a fix that declares a stale instance dead and hands its trigger to
someone else produces two executions of one slot — exactly what
`DisallowConcurrentExecution` was there to prevent. Any recovery threshold has this problem;
the thawed instance has to check that its claim is still its own before committing anything.

## A write that errors may still have been applied

The thaw produced this, from the job's own insert:

```
MongoConnectionException: An exception occurred while receiving a message from the server
 ---> IOException: Unable to read data from the transport connection: Operation canceled
 ---> SocketException (125): Operation canceled
```

Quartz logged `Job DEFAULT.tick threw an exception`. The document is in the collection.

The write reached the server and was applied; the acknowledgement never came back. From the
application's side the insert failed, from the database's side it succeeded. A retry on that
error inserts a second document unless something stops it.

This is why the idempotency key has to be **derived from the slot**, deterministically, and
not be a fresh identifier per attempt. A GUID generated per try collides with nothing and
duplicates happily. It is also why the record goes in before the side effect: after an
ambiguous write you can re-read, but you cannot un-send.

## What 2.2.0-rc.1 changed, measured

Same bench, allocation armed on one pod only so the other survives to be observed.

| | 2.1.0-rc.1 | 2.2.0-rc.1 |
|---|---|---|
| After a kill mid-execution | trigger `Blocked` forever | reclaimed, `Waiting` again |
| Surviving pod | idle four and a half minutes, indefinitely | next slot on time, no restart |
| The slot being executed | lost | lost — `RequestsRecovery` is off |
| Dead instance's scheduler row | stays | removed |
| `LastCheckIn` | written once at startup, never again | maintained, ~15s |

Recovery log from the survivor:

```
Scheduler sla-watcher/pod-b last checked in at 16:14:20Z and is considered failed.
  Reclaiming 1 execution(s).
Reclaimed instance pod-b: 0 released, 0 rescheduled for recovery, 1 abandoned.
```

Slots either side of the kill at 16:14:25: 16:13 fired, **16:14 missing**, 16:15 fired. The
outage is one slot.

## A network cut is the one the process is told about

`docker network disconnect` on the pod that was executing, 2.2.0, same bench. Measured
2026-08-26. The process stays alive, keeps its CPU, and its database calls start failing.

| Time | Event |
|---|---|
| 18:07:00 | pod-b fires, holds. Trigger `Blocked`, `fires` still empty |
| 18:07:05 | pod-b cut from the network |
| immediately | pod-b logs `MongoConnectionException` / `SocketException (125)` out of `QuartzSchedulerThread.Run()` |
| 18:08 | pod-a: `pod-b last checked in at 18:06:58Z and is considered failed. Reclaiming 1 execution(s)` then `1 abandoned` |
| 18:08 | **the 18:08 slot fires on pod-a, on time, no restart** |
| 18:09, 18:10 | schedule continues on pod-a alone |
| 18:10:12 | pod-b reconnected |
| then | pod-b: `Scheduler pod-b/sla-watcher was declared failed by another instance and its work reclaimed. Re-registering.` |

Afterwards: trigger `Waiting`, two scheduler rows checking in, and `fires` holding 18:08,
18:09, 18:10 and 18:11 — one document each. **The 18:07 slot is missing and is not
duplicated.** The outage is one slot, as with the kill.

Three things separate this from the other two failures.

**The process knows.** A kill and a freeze both end in silence; here the exception arrives at
once and names the transport. Anything that wants to react to losing its database — stop
taking work, drain, report unhealthy — has a signal here and has none in the other two.

**The eviction is visible from both sides.** The survivor logs the reclaim, and the returning
instance logs that it was declared failed and re-registers, rather than carrying on as if it
still owned its scheduler row.

**No second execution, but not for the reason that looks obvious.** The thawed pod in the
freeze test finished its slot and wrote its record. Here the held execution also ran to
completion — the hold is a `Task.Delay` and needs no database — and then its write failed,
which is why no document appeared. The store protected the *record*. It did not stop the
*work*. A side effect that does not go through Mongo would have happened anyway, and that is
the case the insert-before-send order exists for.

## Not established

- What the orphaned records cost once there are many of them. On 2.2.0-rc.1 they are cleaned,
  so this only matters for a database that ran on an older version.
- Whether a pod killed *without* `DisallowConcurrentExecution` behaves differently. Not
  tested and not planned: the attribute is required here, and testing a configuration nobody
  will run buys nothing.
