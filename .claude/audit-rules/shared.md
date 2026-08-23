# shared — audit checklist addon

> Always loaded by `/audit`. Turns the generic skill into a reviewer that knows this project.
>
> `(section N)` headings carry rules the audit routes into that part of its checklist.
> `(context)` headings carry facts it needs to read the code at all.
>
> Sections: 1 completeness against the plan · 2 architecture · 3 code quality · 4 code
> placement · 5 security · 6 GDPR and data hygiene · 7 performance and highload · 8 tests ·
> 9 DB and migrations · 10 documentation, CI and shipping
>
> **Path C — new project, no history.** Every rule is `[predicted]`: derived from a decision,
> not from an incident. When one catches a real defect it is retagged `[observed]` with a
> one-line note of what it caught. A `[predicted]` rule with nothing behind it after a year is
> promoted with evidence or deleted.
>
> `[mechanical]` — checkable without opinion, so the audit may fail a review on it.
> `[judgment]` — needs a human to weigh context; the audit raises it and explains, and never
> fails a review on judgment alone.

## Coordinates (context)

- **Project type** — internal.
- **Tracker for this project's own work** — none. Reason: local experiment. No ticket keys in
  commits, and do not look for a board. This is not normal for a project here and does not
  transfer: every other project has Jira.
- **Wiki** — none.
- **Documents** — `DECISIONS.md` at the root, authoritative. If a rule here disagrees with it,
  one of the two is wrong and both change in the same PR.
- **Who to ask** — role: sole maintainer · Ekaterina Kozhina · 2026-08-21.

## External systems (context)

- **Jira, the employer's internal instance — read only.** Not this project's tracker: it is
  the dataset the calculation runs on. Base URL, project keys and token live in local
  configuration only, outside the repository tree.
- **A local Jira double, in compose.** Its purpose is failure injection, nothing else: 429
  with `Retry-After`, a changelog truncated the way search truncates it, two tickets sharing
  an `updated` timestamp. Normal-path behaviour is verified against the real instance, so
  Jira's semantics are observed rather than assumed.
- **MongoDB, in compose.** Job store and application state.

## Project stack taxonomy (context)

- `dotnet` — .NET 9 worker. Quartz 3.19.1, `cotique.Quartz.Spi.MongoDbJobStore` 2.2.0 as the
  job store, MongoDB driver 3.11.

A TypeScript dashboard is planned and deliberately absent. When its first file lands,
`check.sh --layer` must fail on the missing `ts.md`. That failure is a test of the setup — do
not pre-create the addon to avoid it.

Generic stack names that do not apply: `php`, `go`, `python`, `lua`, `rust`.

## Repository layout (context)

One git repository, one solution, no subprojects, no cross-repo contracts. `graphify-out/` is
gitignored.

## Local dev environment (context)

**Mongo is installed locally and development runs against it.** The risk is therefore not a
port collision but a data collision: the database is dedicated to this project and named so
it cannot be mistaken for anything else on that instance, and the collection prefix keeps the
job store's collections apart inside it.

**CI has no Mongo, and never will.** Not a service container either: the workflow runs
`--filter "Category!=Integration"` and nothing that needs a database. That asymmetry is the
point to remember: a connection string that works on a laptop says nothing about CI, because
CI never opens one.

The Jira double runs from `docker-compose.yml` in both places.

## Data handling (sections 5 and 6)

The service reads internal company data: ticket text written by colleagues, and who is
assigned to what.

- [predicted] [mechanical] **Credentials are local-only** — `dotnet user-secrets` or the environment, never
  `appsettings*.json`, never compose, never a test file.
- [predicted] [judgment] **No captured responses.** Fixtures are hand-written and synthetic. Recording a
  real API response into the repo is the easy accident: it lands as JSON nobody reads again,
  carrying real ticket text and real names.
- [predicted] [mechanical] **No real issue keys** in code, tests, fixtures or commit messages. The double
  generates keys in a format that cannot collide with the real ones.
- [predicted] [mechanical] **Free text is never logged.** Summaries, descriptions and comments do not reach
  a log line, not at debug level and not temporarily.
- [predicted] [mechanical] **People are not stored.** Assignee, reporter and comment authors are colleagues'
  names. They are not written to Mongo and not aggregated into anything the repo keeps.
- [predicted] [mechanical] **Logs and backups count as storage.** Any rule above that says "not stored"
  means not there either.

## Environment files (section 4)

`.env` is gitignored; `.env.example` is the only checked-in template. A new setting goes into
`.env.example`, and into `DECISIONS.md` when it changes behaviour.

## CI (section 10)

**CI is build and test. Assume there is no database and no infrastructure there, ever.**
This is a standing decision, not a current limitation: a runner that grows service
containers has a test suite that lost its split, and the fix is the suite, not the workflow.

- GitHub Actions, one workflow.
- [predicted] [mechanical] The workflow runs `.claude/check.sh --layer` before the build. A
  change that adds a stack without its addon fails CI rather than merging quietly.
- [predicted] [mechanical] **Tests are split by trait.** Anything needing Mongo or the Jira
  double is `[Trait("Category", "Integration")]`; CI runs `--filter "Category!=Integration"`.
  An integration test without the trait runs on a runner with no database.
- [predicted] [mechanical] **An integration test whose dependency is absent fails loudly.** It
  never skips itself and never passes. A suite that goes green because Mongo was missing is
  the worst outcome available here — it is the canary rule applied to the test harness.
- Integration tests are a **local gate**, run before pushing, against the installed Mongo and
  the double.

## Documentation (section 10)

`DECISIONS.md` records decisions and the reason each was forced. A reversed decision is
rewritten with the new reason, not deleted — the reversal is the interesting part.

## graphify (context)

One `graphify-out/`, gitignored, rebuilt on demand. The graph is a function of the commit, so
there is nothing to share and a committed one would go stale on every merge.

## Knowledge store (context)

**mempalace, local — one store per developer**, through `.claude/kb/mcp-router.sh`. What is
written there is visible only to its author, so anything that has to be shared goes into this
repo as a file. The exception is internal company facts: those go into neither, and stay in
the local store.

- Before changing a module: search for prior findings. A miss is a result — record it.
- After a decision or a non-obvious fix: duplicate-check, then write.
- Temporal facts carry a valid-from; on change, invalidate rather than delete.

## Communication language (section 3)

English in code, comments, commit messages and PR descriptions. No em dashes in prose. No
tracker IDs anywhere — there is no tracker, and the internal Jira's keys are not ours to use.

## Comment hygiene (section 3)

No absolute or per-user paths in code, config literals or string defaults. Pointers to real
files in this repo are encouraged; they survive refactors.

## Naming gotchas (section 3)

`SlaWatcher` is the assembly and root namespace. **Quartz persists job type names as strings
in Mongo**, so renaming a job class orphans its stored triggers — they do not error, they stop
firing.

## Tests (section 8)

- xUnit. Unit tests need nothing; anything touching the job store or Mongo is an integration
  test against compose.
- [predicted] [mechanical] The three failure behaviours — missed fire, duplicate fire, 429 — each have a
  test that causes the failure on purpose. The double has a control endpoint for exactly that.
  A handler that has never been triggered is not a handler, it is code.
- [predicted] [mechanical] No `Task.Delay` in tests. Use a controllable clock or wait on a signal.

## Verification and evidence (section 8)

- **Canary rule.** No green signal counts as evidence until it has been made to go red once.
  For a new xUnit test: assert the opposite, watch it fail, then invert. A suite that passes
  with a deliberately broken assertion never ran.
- Claims about behaviour carry the command that produced them. "Tests pass" without the runner
  output is not evidence.

## Pre-shipping checks (section 10)

`dotnet build -warnaserror`, the test suite, and `.claude/check.sh --layer` all green. The
compose stack comes up from cold on a machine that has never run it.

---

## Rules forced by the decisions

Each maps to an entry in `DECISIONS.md`. These are the load-bearing ones.

- [predicted] [mechanical] **(2) Misfire policy is `WithMisfireHandlingInstructionDoNothing`** on the
  polling trigger. A trigger that catches up missed fires re-reads the same window and
  re-sends. Flag any other misfire instruction there.
- [predicted] [mechanical] **(2) The watermark advances only after a page is fully processed.** Advancing
  earlier loses tickets on a crash. Flag any watermark write that is not after the last item.
- [predicted] [mechanical] **(2) Escalations are insert-then-send.** The idempotency document is written
  first, the send happens after. Reversing it turns a crash into a duplicate notification —
  and the reversal looks like a simplification, which is why this is the rule most likely to
  be broken by a refactor.
- [predicted] [mechanical] **(9) The idempotency key has a unique index.** Without it the rule above is
  decorative: two clustered instances both insert.
- [predicted] [judgment] **(7) The job is `[DisallowConcurrentExecution]` and has a runtime cap.** A
  throttled run must not overlap the next fire.
- [predicted] [mechanical] **(7) 429 is handled with `Retry-After`.** A fixed delay is a guess dressed as
  compliance.
- [predicted] [mechanical] **(2) Status age comes from the issue changelog**, fetched per issue, never from
  `updated`. Search truncates the changelog when `changelog.total > maxResults`.
- [predicted] [mechanical] **(2) Age is measured in the reviewer's working hours** via a Quartz calendar.
  Calendar hours count nights and weekends and turn a flat metric into a trend.

---

## Where the data lives, decided (sections 5 and 6)

Answered during the intake, 2026-08-21.

- [predicted] [mechanical] **A developer's machine uses a live read-only connection** to the internal Jira
  for normal behaviour, and the double for failure injection. Consequence: real data does
  reach a laptop, so every rule in "Data handling" applies to a machine nobody administers.
- [predicted] [mechanical] **CI uses the double only. No credentials exist in CI**, and no job reaches the
  internal network. The contract test against the real instance runs locally and deliberately.
  A workflow that adds a Jira secret is a finding, not a convenience.
- [predicted] [mechanical] **Stored: issue key, transition timestamps, the watermark, escalation records.
  Transit-only: summaries, descriptions, comments, and any person's name.**

⚠️ **The known weak point, recorded rather than resolved.** An issue key resolves to a person
through Jira itself, so treating it as non-personal is an interpretation. It holds while the
data stays on one machine and nothing derived from it is published. If either changes — a
shared deployment, an export, a dashboard — this decision is reopened, and the alternative is
storing a hash instead of the key, at the cost of not being able to tell from a log which
ticket a run was about.
