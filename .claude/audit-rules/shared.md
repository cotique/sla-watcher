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
> A rule is `[predicted]` while it is derived from a decision rather than from an incident.
> When one catches a real defect it is retagged `[observed]` with a one-line note of what it
> caught. A `[predicted]` rule with nothing behind it after a year is promoted with evidence
> or deleted.
>
> `[mechanical]` — checkable without opinion, so the audit may fail a review on it.
> `[judgment]` — needs a human to weigh context; the audit raises it and explains, and never
> fails a review on judgment alone.

## Project stack taxonomy (context)

- `dotnet` — .NET 9 worker. Quartz 3.19.1, `cotique.Quartz.Spi.MongoDbJobStore` 2.2.0 as the
  job store, MongoDB driver 3.11.

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

## Data handling (sections 5 and 6)

What the code is allowed to keep, and what it must never write down.

- [predicted] [mechanical] **Credentials are local-only** — `dotnet user-secrets` or the environment, never
  `appsettings*.json`, never compose, never a test file.
- [predicted] [judgment] **No captured responses.** Fixtures are hand-written and synthetic. Recording a
  real API response into the repo is the easy accident: it lands as JSON nobody reads again,
  carrying whatever the response happened to contain.
- [predicted] [mechanical] **No real issue keys** in code, tests, fixtures or commit messages. The double
  generates keys in a format that cannot collide with the real ones.
- [predicted] [mechanical] **Free text is never logged.** Summaries, descriptions and comments do not reach
  a log line, not at debug level and not temporarily.
- [predicted] [mechanical] **People are not stored.** Assignee, reporter and comment authors are real
  names. They are not written to Mongo and not aggregated into anything the repo keeps.
- [predicted] [mechanical] **Logs and backups count as storage.** Any rule above that says "not stored"
  means not there either.

## Environment files (section 4)

`.env` is gitignored; `.env.example` is the only checked-in template. A new setting goes into
`.env.example`, and into the README when it changes how the service is run.

## CI (section 10)

**CI is build and test. Assume there is no database and no infrastructure there, ever.**
This is a standing decision, not a current limitation: a runner that grows service
containers has a test suite that lost its split, and the fix is the suite, not the workflow.

- GitHub Actions, one workflow.
- [predicted] [mechanical] The workflow runs `.claude/check.sh --layer` before the build. A
  change that adds a stack without its addon fails CI rather than merging quietly.
- [predicted] [mechanical] **Tests are split by trait.** Anything needing Mongo is
  `[Trait("Category", "Integration")]`; CI runs `--filter "Category!=Integration"`.
  An integration test without the trait runs on a runner with no database.
- [predicted] [mechanical] **An integration test whose dependency is absent fails loudly.** It
  never skips itself and never passes. A suite that goes green because Mongo was missing is
  the worst outcome available here — it is the canary rule applied to the test harness.
- Integration tests are a **local gate**, run before pushing, against the local MongoDB.

## Documentation (section 10)

A measurement goes to `docs/` with what was observed and what it cost. A reversed rule is
rewritten with the new reason rather than deleted: the reversal is the interesting part.

## graphify (context)

One `graphify-out/`, gitignored, rebuilt on demand. The graph is a function of the commit, so
there is nothing to share and a committed one would go stale on every merge.

## Knowledge store (context)

**mempalace, local — one store per developer**, through `.claude/kb/mcp-router.sh`. What is
written there is visible only to its author, so anything that has to be shared goes into this
repo as a file.

- Before changing a module: search for prior findings. A miss is a result — record it.
- After a decision or a non-obvious fix: duplicate-check, then write.
- Temporal facts carry a valid-from; on change, invalidate rather than delete.

## Communication language (section 3)

English in code, comments, commit messages and PR descriptions. No em dashes in prose. No
tracker IDs anywhere.

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
- [predicted] [mechanical] No `Task.Delay` in tests. Use a controllable clock or wait on a signal.

## Verification and evidence (section 8)

- **Canary rule.** No green signal counts as evidence until it has been made to go red once.
  For a new xUnit test: assert the opposite, watch it fail, then invert. A suite that passes
  with a deliberately broken assertion never ran.
- Claims about behaviour carry the command that produced them. "Tests pass" without the runner
  output is not evidence.

## Branching and merging (section 10)

- [observed] [mechanical] **`master` is the only long-lived branch, and it advances only through
  a merged pull request.** Not `git push origin master`, and not the merges API either: that was
  a merge made that way leaves a merge commit with no page, no diff to open and nowhere to
  leave a comment.
- [observed] [mechanical] **The branch name carries the kind**: `feat/`, `fix/`, `chore/`,
  `docs/`, `plan/`, and `store/2.2.0` shaped names for taking a new version of the job store. A
  generated session name, anything under `claude/` with a random suffix, never reaches a
  remote: rename it before the first push.
- [predicted] [judgment] **One deliverable per branch.** A stage of a plan is a branch.
- [predicted] [mechanical] **Delete the branch on both sides once it is merged.** A merged branch
  left on the remote reads as work in flight.
- [observed] [mechanical] **`master` is protected server-side, and only because this repository
  is public.** Branch protection and rulesets are gated by plan on a private repo, and this one
  was private at first: three endpoints answered `Upgrade to GitHub Pro or make this repository
  public` with a token that already carried the `repo` scope. Going public enabled it —
  force-push and deletion disabled, the `build` check required, `enforce_admins` on. Local
  configuration still matters alongside it: `.githooks/pre-push`, enabled with `core.hooksPath`,
  catches the same mistake before the round-trip to the server, so every clone turns it on once
  for itself.
- [observed] [mechanical] **The same hook refuses a tag push.** A tag publishes, so it is created
  deliberately rather than swept along with a branch. Nothing is released from this repository
  yet; the store repository is where that matters, and where a refused tag push would be felt.
- [predicted] [judgment] **Force-pushing a branch that is under review** is for correcting that
  branch's own mistake, and it is said out loud in the pull request when it happens.

## Code style (section 3)

Rules that came out of review rather than from a style guide, so each one has a reason and none
of them is taste.

- [observed] [mechanical] **An immutable string is built in one expression.** Not a literal plus
  a literal plus a literal across three lines: build the whole value once and let it read as the
  thing it produces. Checked by `.claude/style.sh`.
- [observed] [judgment] **No magic numbers.** A literal that carries meaning is a named constant,
  and the name says why the value is what it is rather than repeating the digits. Bounds on a
  `Range` attribute included. Not mechanically checkable, so the audit raises it and a human
  decides.
- [observed] [mechanical] **A counter that is read by a person starts at one**, so a log line
  needs no arithmetic to be legible. A loop that counts from zero and then adds one in the
  message is doing in the message what the starting value should do.
- Documentation comments are the author's, and a formatting pass over them is not a review
  finding. Left deliberately out of `style.sh`.
- The em dash rule under "Communication language" applies to **new** prose and is not enforced
  by anything: it was already broken across the repository before anything checked, and
  rewriting every file to satisfy a script is not worth the diff.

## Pre-shipping checks (section 10)

`dotnet build -warnaserror`, the test suite, `.claude/check.sh --layer` and `.claude/style.sh`
all green. The compose stack comes up from cold on a machine that has never run it.

---

## Load-bearing rules

Break one of these and the failure is silent.

- [predicted] [mechanical] **Misfire policy is `WithMisfireHandlingInstructionDoNothing`** on the
  polling trigger. A trigger that catches up missed fires re-reads the same window and
  re-sends. Flag any other misfire instruction there.
- [observed] [mechanical] **A record keyed by the work, never by the attempt.** `FireLog.SlotKey`
  derives the key from the trigger and the scheduled instant, so a retry after an ambiguous
  write collides instead of adding a row.
- [predicted] [judgment] **The job is `[DisallowConcurrentExecution]` and has a runtime cap.** A
  throttled run must not overlap the next fire.
- [predicted] [mechanical] **429 is handled with `Retry-After`.** A fixed delay is a guess dressed as
  compliance.
- [predicted] [mechanical] **Status age comes from the issue changelog**, fetched per issue by
  `IIssueSource.GetStatusTransitionsAsync`, never from `updated`.
- [observed] [mechanical] **Age is measured in working hours** by `WorkingHours.Between`, not by
  a Quartz calendar: `ICalendar` answers only whether one instant is included.

---

