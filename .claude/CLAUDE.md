# sla-watcher — project context

> Process rules are in `CLAUDE.md` at the repo root. Project specifics only here.
> The template is next to this file; the diff between them shows what the bundle has added.

## Project overview

A worker built on Quartz with a MongoDB job store. It runs work on a schedule across several
instances, and measures how long a ticket sat in a status **in working hours**.

**Single git repository**, one solution, no subprojects.

- `src/SlaWatcher/` — .NET 9 worker. Quartz 3.19.1 with `cotique.Quartz.Spi.MongoDbJobStore`
  2.2.0 as the job store, driver 3.11.
- `tests/SlaWatcher.Tests/` — xUnit. Integration tests carry
  `[Trait("Category", "Integration")]` and run against the locally installed Mongo on 27117,
  which is where the bench runs too; CI never sees them.

This repository is a testbed; see the README for what it is testing.

## Why the service has no model in it

It is deliberately boring. A model inside the product would make every "why did this fire
twice" start with ruling out the model.

## Commands

**Environment (repo root):**
```bash
docker compose up -d mongo                       # MongoDB on 27117
docker compose --profile pods up -d --build      # the two-instance failure bench
```

**Run (repo root or the project dir):**
```bash
DOTNET_ENVIRONMENT=Development dotnet run --project src/SlaWatcher
```
In Visual Studio the run profile does it. The file is gitignored and not shipped in any
form — everyone makes their own, once, at `src/SlaWatcher/Properties/launchSettings.json`:

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

Configuration, and the asymmetry is deliberate:

| File | Holds | Committed |
|---|---|---|
| `appsettings.json` | logging, instance name, cron. **No connection string.** | yes |
| `appsettings.Development.json` | the local Mongo on 27117, no credentials in it | yes |
| user-secrets / environment | anything with a token in it | never |

`appsettings.json` has no connection string **on purpose**: a run outside Development fails
at boot with a validation error instead of quietly pointing at something that happens to
answer on the default port.

**Build and test:**
```bash
dotnet build -warnaserror
dotnet test                                  # everything, needs the local MongoDB
dotnet test --filter "Category!=Integration" # what CI runs: no database, no infrastructure
```

**The layer:**
```bash
.claude/check.sh --layer      # is the project layer complete
.claude/check.sh --machine    # can this machine run it
```

## What is unusual here, and will bite

- **Job type names are stored data.** Quartz persists the CLR type name in Mongo. Rename a job
  class and its triggers are orphaned: they do not error, they stop firing.
- **A watchdog must not be a job.** `StuckExecutionMonitor` runs on its own timer, because a
  job that never finishes is what would stop a Quartz-scheduled watchdog from running.
- **`StuckExecutionProbe` reads the job store's own collection.** That is coupling to another
  package's storage, and the integration test writes the document shape it depends on so a
  store upgrade fails there rather than silently.

## Knowledge base

mempalace, **local** — one store per developer, through `.claude/kb/mcp-router.sh`. Call names
are in the root `CLAUDE.md`. Facts about the tracker's contents stay in the store and go into
neither the repo nor a shared place.
