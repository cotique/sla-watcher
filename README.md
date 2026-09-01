# sla-watcher

A .NET 9 worker built on Quartz with a MongoDB job store. It runs work on a schedule across
several instances, and measures how long a ticket sat in a status **in working hours** rather
than calendar hours.

The service is deterministic. There is no model inside it, deliberately: a scheduling question
should never begin by ruling out a model.

**This repository is a testbed**, for two things at once: the `.claude/` agent-tooling layer
(rules, hooks, audit) running against real work, and Quartz's clustering behaviour on a
MongoDB job store under real failure, not a tutorial.

**In progress.** What is here: the scheduler and its clustering behaviour, measured under
three kinds of instance failure; the working-hours arithmetic; a tracker client with its
paging and throttle handling; and a watchdog for executions that never finish.

## Run it locally

Requires .NET 9, Docker Desktop, and nothing else.

```bash
docker compose up -d mongo                                  # MongoDB on 27117, this project only
DOTNET_ENVIRONMENT=Development dotnet run --project src/SlaWatcher
```

The connection string lives in `appsettings.Development.json`. `appsettings.json` deliberately
has none, so a run outside Development stops at boot with
`ValidationException: The MongoConnectionString field is required` rather than connecting to
whatever answers on the default port.

Anything with a token in it goes to user-secrets or the environment, never to a file in the
repository.

In Visual Studio, create your own `src/SlaWatcher/Properties/launchSettings.json`; it is
gitignored and not shipped in any form:

```json
{
  "profiles": {
    "SlaWatcher": {
      "commandName": "Project",
      "environmentVariables": { "DOTNET_ENVIRONMENT": "Development" }
    }
  }
}
```

Note that a launch profile **overrides** the ambient `DOTNET_ENVIRONMENT`. To test anything
environment-dependent, pass `--no-launch-profile`.

## Test it

```bash
dotnet test                                   # everything, needs the local MongoDB
dotnet test --filter "Category!=Integration"  # what CI runs: no database, no infrastructure
```

Integration tests carry `[Trait("Category", "Integration")]` and use their own database, not the
bench's. When the database is absent they **fail**; they never skip. A suite that goes green
because MongoDB was missing is the worst outcome available here.

New tests are watched failing before they are believed. `.claude/audit-rules/shared.md` calls
that the canary rule, and it is the one convention worth keeping if every other one is dropped.

## Check it

```bash
dotnet build -warnaserror     # the build gate
dotnet format --verify-no-changes
.claude/check.sh --layer      # is the agent layer complete
.claude/check.sh --machine    # can this machine run the project
.claude/style.sh              # the style rules a compiler does not enforce
```

GitHub's secret scanning and push protection are also on, free on a public repo: a credential
matching a known pattern is refused at push time, not found afterwards in the history.

## The failure bench

Two containers against one database, for the failure behaviour that a single process cannot
show. They do not start with the stack, and they have no restart policy on purpose: their death
is the experiment, and an automatic restart hides it.

```bash
docker compose --profile pods up -d --build
docker compose --profile pods rm -sf pod-a pod-b   # never `docker compose down`, it takes MongoDB with it
```

What has been measured on it, and what each failure costs, is in
`docs/pod-death-and-the-job-store.md`.

## Branching and merging

`master` is the only long-lived branch and advances only through a merged pull request. Branch
names carry the kind: `feat/`, `fix/`, `chore/`, `docs/`, `plan/`, and `store/2.2.0` shaped
names for taking a new version of the job store.

GitHub enforces this server-side: `master` is protected, force-push and deletion are disabled,
the `build` status check must be green before a merge, and the protection applies to the repo
owner as well (`enforce_admins`). That protection is only available because this repository is
public; on a private repo it is gated by plan, and until this one went public that gate was
closed.

A push to `master` is also refused locally by `.githooks/pre-push`, enabled with:

```bash
git config core.hooksPath .githooks
```

Run that once per clone. It is local configuration and cannot be inherited, and it catches the
same mistake before the round-trip to GitHub, but the server-side rule above is the one that
actually holds. The same hook refuses a tag push, because a tag publishes.

## Where it deploys

Nowhere yet, and there is no deployment decision on record. Three constraints are already known
from measurement and any deployment has to respect them:

- every instance needs a distinct `quartz.scheduler.instanceId`; `AUTO` resolves to the literal
  `NON_CLUSTERED` and two instances sharing one identity break recovery
- a rolling update is a mixed-version upgrade, and mixed versions of the job store are unsafe.
  Stop the old instances before starting the new ones
- watch `quartz.firedTriggers` for documents in `Executing` older than a job could plausibly
  run. Nothing else reports a stopped schedule: no error, no log line, no failed health check.
  `StuckExecutionMonitor` does this from inside the service, on its own timer, and logs at
  error. It is the only witness to an instance that is alive with a job wedged inside it,
  because the store's recovery reclaims work only from an instance that stopped checking in

## Where things are written down

| File | Holds |
|---|---|
| `docs/pod-death-and-the-job-store.md` | what three kinds of instance failure actually cost, measured |
| `.claude/audit-rules/shared.md` | project rules the audit reads: data handling, CI, branching, style |
| `.claude/audit-rules/dotnet.md` | the stack rules, with the ones learned from a real failure tagged |
| `.claude/CLAUDE.md` | project context for an agent working here |

Plans and handover briefs are deliberately **not** tracked: they are written to be acted on and
then to go stale, and a stale plan in a repository reads as a description of the code.
