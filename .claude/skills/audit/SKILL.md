---
name: audit
description: "Post-implementation audit. Run AFTER finishing a plan from docs/plans/<feature>/ and BEFORE reporting done. Reads git diff, detects changed stacks, loads project-specific addons from .claude/audit-rules/, walks a 10-section checklist (completeness vs plan, architecture, code, placement, security, GDPR, performance, tests, DB/migrations, docs), reports issues as `file:line + problem + recommendation`, and applies fixable issues in place. Scope auto-shrinks to changed stacks; pass `all` to force full coverage."
allowed-tools:
  - Bash
  - Read
  - Grep
  - Glob
  - Edit
  - Write
---

# audit

Post-implementation audit. The user has finished implementing a plan and wants a structured pass over the result before reporting back.

## When to invoke

- User says "audit", "check the implementation", "the plan is done, review it", or any plan-done intent.
- After you (the agent) implement a plan from `docs/plans/<feature>/` and BEFORE reporting "done".
- When release-intent or pre-merge phrasing fires.

## Arguments

The skill accepts optional whitespace-separated tokens:

- (no args)              auto-scope from `git diff`
- `all`                  load every addon in `.claude/audit-rules/`, audit every stack
- `<stack>` `<stack>`... explicit stack list (e.g. `fe`, `be fe`, `go python`)
- `--base=<ref>`         override the diff base (default: `origin/main` if present, else `main`, else `HEAD~1`)
- `--no-fix`             report only, do not Edit/Write fixes

## Step 1. Locate the project root and rules root

```bash
ROOT="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"

# Walk up from $ROOT looking for the nearest ancestor that contains
# `.claude/audit-rules/`. This supports monorepo layouts where the parent
# workspace dir holds the rules and each subdir is its own git repo. Stop
# at filesystem root.
RULES_ROOT=""
dir="$ROOT"
while [ "$dir" != "/" ] && [ -n "$dir" ]; do
    if [ -d "$dir/.claude/audit-rules" ]; then
        RULES_ROOT="$dir"
        break
    fi
    dir="$(dirname "$dir")"
done
```

`$ROOT` keeps pointing at the current git toplevel (used for `git diff`,
`git status`, file-path resolution). `$RULES_ROOT` is the nearest ancestor
that ships audit addons — it may equal `$ROOT` (single-repo project) or
sit one or more levels above (monorepo workspace).

If `$RULES_ROOT` is empty, stop. Tell the user:
"No `.claude/audit-rules/` found at $ROOT or any ancestor. Create it (with
one addon file per stack — `be.md`, `fe.md`, `shared.md`, …) at the project
root or at a workspace dir above it before running audit."

## Step 2. Find the plan

Look for `docs/plans/<feature>/` directories that match recent activity.
Check both the current project root and the workspace root (in monorepos,
the plan dir often lives one level up alongside `docs/`):

- `$ROOT/docs/plans/`
- `$RULES_ROOT/docs/plans/` (if different from `$ROOT`)

Heuristics:
- The most recently modified subdir of either `docs/plans/`.
- The path referenced in the agent's prior planning turns.
- If unsure, ask the user: "Which plan? Found: <list>".

Read every `.md` in that plan directory. Hold the plan in mind as the source of truth for section 1 (Completeness).

## Step 3. Auto-detect changed stacks

If the user didn't pass an explicit stack list and didn't pass `all`:

```bash
BASE="${BASE:-$(git rev-parse --verify origin/main 2>/dev/null \
            || git rev-parse --verify main 2>/dev/null \
            || git rev-parse HEAD~1)}"
git diff --name-only "$BASE"...HEAD
git status --porcelain | awk '{print $2}'
```

Merge committed + uncommitted changes. Group files into stacks:

Default mapping, by extension:

| Path pattern                              | Stack    |
|------------------------------------------ |----------|
| `**/*.php`                                | `php`    |
| `**/*.ts`, `**/*.tsx`                     | `ts`     |
| `**/*.cs`                                 | `dotnet` |
| `**/*.go`                                 | `go`     |
| `**/*.py`                                 | `python` |
| `**/*.lua`                                | `lua`    |
| `**/*.rs`                                 | `rust`   |
| `**/openapi.yaml`, `**/migrations/**`, `**/*.sql` | `shared` |
| `docs/**`                                 | `shared` |
| `.github/workflows/*.yml`, `.gitlab-ci.yml`       | `shared` |
| `package.json`, `composer.json`, `*.csproj`, `go.mod`, `pyproject.toml` | `shared` (dep change) |

**Override this table in `.claude/audit-rules/shared.md` when the project groups code by
application directory instead of by extension.** A repo laid out as `be-app/` and `fe-app/`
maps `**/be-app/**/*.php → be` and `**/fe-app/**/*.tsx → fe`, and its addons are then named
`be.md` and `fe.md`. The addon filename must match the stack name this table produces,
otherwise the addon silently never loads.
| Everything else                           | `shared` (config/scripts) |

`shared` is always included regardless of what else changed.

## Step 4. Load addons

For each detected stack `S`:

```bash
ADDON="$RULES_ROOT/.claude/audit-rules/${S}.md"
[ -f "$ADDON" ] && cat "$ADDON"
```

Always load `$RULES_ROOT/.claude/audit-rules/shared.md`.

The addons are NOT rules to repeat back. They are bullet checklists that specialize the generic sections below for this project + stack.

## Step 5. Walk the 10 sections

Run each section against the changed files only. Use the addon bullets as specialization. For every issue, output:

```
- <relative/path>:<line> — <problem>. → <recommendation>
```

A section with no issues outputs `- Verified.`

### 1. Completeness vs plan
Match each plan item against the implementation. List items that are: done, partial (what's missing), changed (what differs and why), missing entirely.

### 2. Architecture
Per stack, verify the structural rules from the addon (e.g. for `be`: thin controllers, logic in services, queries in repositories; for `fe`: FSD downward-only imports, etc.). Skip sections for stacks that didn't change.

### 3. Code quality
Per stack: typing, naming, no duplication, no magic strings/numbers, no anonymous response classes, consistency with surrounding code, debug output removed, dead code cleaned.
- Logging present (server-side / backend stacks): every new operation, error path, and external / integration call emits a log at the right level — business events at `info`, recoverable issues at `warning`, failures at `error` — each with a structured context array of ids / uuids (never PII). A new service method, job handler, or failure branch that runs silently is an audit finding, not "clean" code. Frontend is exempt — browser `console` logging is forbidden there; new failure paths route through the error boundary / error reporter instead.

### 4. Code placement
Per stack: business logic where the addon says it should live, no ORM in services, no API calls in components, Request/Response classes present per endpoint, jobs idempotent and re-throw exceptions.

### 5. Security
- RBAC: endpoints guarded, roles checked.
- Input validation at boundaries.
- No mass assignment, no SQL/XSS/CSRF/IDOR holes.
- No PII in logs (log ids/uuids instead).
- Soft vs hard delete intent matches the spec.

### 6. GDPR / Data hygiene
- Deletable data does not contain PII that must be retained.
- If it does, the implementation includes anonymization or correct deletion.
- Cookie/consent surfaces untouched unless plan said so.

### 7. Performance & Highload
- Batch processing for bulk operations, no per-row loops.
- Indexes on every `WHERE` / `JOIN` / `ORDER BY` column.
- Cache invalidation after every mutation (region-aware when relevant).
- No N+1.
- Background jobs do not block the main request path.

### 8. Tests
For every new/modified class, component, hook, endpoint, migration:
- A test exists (unit / feature / integration / e2e per addon's rules).
- Happy path + edge cases (empty list, already-deleted, concurrent execution, boundary dates).
- Tests pass on the relevant module (don't run the full suite — see project CLAUDE.md for per-module test commands).

### 9. DB & migrations (only if `shared` had migration changes)
- Migrations reversible.
- Indexes added.
- No edits to applied migrations under `app/migrations/Data/` (or the project's frozen-migration path per addon).

### 10. Documentation
- OpenAPI regenerated if endpoints changed.
- Architecture docs (`docs/`, `ArchitectureBackend.md`, `ArchitectureFrontend.md`, addon-specific paths) updated if structure changed.
- Plan file updated with any deliberate divergences.

## Step 6. Apply fixes

For each issue that is fixable in place (debug output removal, missing types, missing data-testid, missing imports, missing test cases that mirror an existing test, missing OpenAPI annotations), apply the fix via Edit/Write. Record the fix in the report as `FIXED:` prefix.

For each issue that requires a judgment call (renaming a class, restructuring a module, security hardening with options), do NOT auto-fix. Record as `OPEN:` prefix with the recommendation.

Honor `--no-fix` if the user passed it.

## Step 7. Final report

Structure:

```
=== AUDIT: <feature-name> ===
Plan: docs/plans/<feature>/
Base: <ref>
Changed stacks: <list> (file counts per stack)
Loaded addons: <list>

Section 1: Completeness vs plan
  - <issues or "Verified.">

Section 2: Architecture
  Stack: be
    - <issues or "Verified.">
  Stack: fe
    - <issues or "Verified.">

...

Summary:
  N issues FIXED in-place.
  M issues OPEN (need user decision).
  K stacks not audited (no changes detected): <list>.
  Next step: <user-actionable>.
```

End the report with a one-line recommendation: "All clear, safe to report done." OR "Please review the M open issues before reporting done."

## Notes

- Never invent rules. If a section has no addon bullet and no universal rule from the body above, write `- N/A for this stack.` and move on.
- Read code, not memory. Every claim must reference a file:line.
- Use the project's `CLAUDE.md` for project conventions; do not duplicate them in the report.
- If the diff is large (>200 files), warn the user and suggest narrowing scope via `/audit <stack>` instead of running across everything.
