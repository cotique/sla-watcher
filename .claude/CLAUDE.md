# sla-watcher — project context

> Process rules are in `CLAUDE.md` at the repo root. Project specifics only here.
> Written by the intake, 2026-08-21, path C. The template is next to this file; the diff
> between them shows what the bundle has added since.

## Project overview

A worker that polls a ticket tracker on a schedule, measures how long tickets have sat in a
status **in the reviewer's working hours**, and raises an escalation when one leaves the band.

**Internal project. Single git repository**, one solution, no subprojects.

- `src/SlaWatcher/` — .NET 9 worker. Quartz 3.19.1 with `cotique.Quartz.Spi.MongoDbJobStore`
  2.2.0 as the job store, driver 3.11.
- `tests/` — xUnit. Integration tests take the compose services.
- `tools/jira-double/` — the local Jira stand-in: the two endpoints the worker calls, plus a
  control endpoint that makes them fail on demand.

A TypeScript dashboard is planned and deliberately absent. Its first file must make
`check.sh --layer` fail on the missing `ts.md` addon — that failure is a test of the setup.

## Why this project exists

Two goals in order: find out whether the audit rules catch anything real, and learn Quartz
with MongoDB on .NET. **The service is deliberately boring — no AI inside it.** The agent
writes the code under the rules; a model inside the product too would make every "why did
this fire twice" start with ruling out the model.

Decisions and the reason each was forced: `DECISIONS.md` at the root, authoritative.

## Commands

**Environment (repo root):**
```bash
docker compose up -d          # the Jira double; Mongo is installed locally, not here
docker compose down -v        # and throw its data away
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
dotnet test                                  # everything, needs local Mongo + the double
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
- **The watermark is the recovery mechanism.** A missed fire is harmless because the next run
  takes a wider window. That is why the misfire policy is "do nothing" and why the watermark
  advances only after a page is fully processed.
- **Insert before send, always.** Reversing it turns a crash into a duplicate notification,
  and the reversal looks like a simplification.
- **The double is for failure injection, not for normal behaviour.** Normal paths are verified
  against the real Jira, so its semantics are observed rather than assumed.

## Data

Reads the employer's internal Jira, read-only. Credentials are local-only. Free text and
people's names are transit-only: not stored, not logged. Full rules in
`.claude/audit-rules/shared.md`, section "Data handling" — and three questions there are still
open, so check before writing anything that persists a field.

## Knowledge base

mempalace, **local** — one store per developer, through `.claude/kb/mcp-router.sh`. Call names
are in the root `CLAUDE.md`. Internal company facts stay in the store and go into neither the
repo nor a shared place.
