# Shared Agent Rules

## Communication Language

Code, comments, and git messages: in English.
Communicate with the user in their language.

## Agent Work Rules

### Planning

- Non-trivial tasks (>3 files or >100 lines): **read or create a plan** in `docs/{task}/` before coding. Align with user on approach. See project CLAUDE.md for internal structure conventions.
- **Scope strictly**: one deliverable per session.
- **Context preservation**: if session touches >5-7 files, split into multiple sessions.
- Plan order: Read spec → Read ALL files to touch → Map dependencies → Plan → Audit.
- **Plan files over full docs/RFCs**: if a plan exists, read it first. Consult full docs only when the plan doesn't answer.
- **Surface tradeoffs, don't silently pick.** If multiple viable approaches exist, present them with tradeoffs before implementing. If a simpler approach solves the problem, propose it and push back instead of accepting more complexity than the task needs.
- **Define success criteria before coding.** For multi-step changes, state a brief plan like `1. Step → verify: check` and loop until each step verifies. "Make it work" is a weak target; tests, curl probes, pipeline runs are strong ones.

### Parallelism & Delegation

- **Max 5 sub-agents.** Don't delegate <50 lines or single-file changes.
- Delegate when: 5+ files to read, 3+ to write, or genuinely independent work (e.g. BE/FE split).

### Context Management

- **Never modify unread files** — always read first.
- **Pattern-first**: read ONE existing example of the same kind, not all. Match its style even if you would do it differently — personal taste does not override consistency with the codebase.
- **Minimize reads**: don't read entire directories.
- **Shared files** (configs, route registrations, barrel exports): one session only, or sequential.

### Code Quality

- **Remove debug output** (`console.log`, `var_dump`, `print_r`, `dumper.print_r`) before finishing.
- **Clean up YOUR orphans** — imports, variables, functions that your changes made unused. Do not delete pre-existing dead code unless the user asked. If you notice unrelated dead code, mention it, don't touch it.
- **No magic numbers/strings** — use named constants.
- **No speculative code** — don't build for hypothetical future requirements.
- **Surgical changes** — every changed line should trace directly to the user's request. Don't "improve" adjacent code, formatting, or comments while you're in the file.
- **No ticket / plan references in code**: comments and identifiers must not reference internal trackers (`ABC-1234`, `JIRA-XXX`, etc.) or planning artefacts (`Plan sect 6.1`, `Plan AC #N`, `AC #N`, `docs/plans/...`, `be-plan.md`, `fe-plan.md`). Describe what the code does, not which plan section spawned it. Tracker IDs go in commit messages and PR descriptions, not in checked-in source. Referencing real code paths in the same repo (e.g. `src/test/integration/helpers/transactions.lua`) is fine and encouraged.
- **Relative paths only**: never bake absolute or per-user paths (`/home/alice/...`, `/Users/alice/...`, `C:\...`, `~/...`) into code, comments, or config. Use project-relative paths or env vars.
- **Security**: validate at system boundaries, sanitize inputs, use parameterized queries.
- **Don't commit `.env`** or push directly to `main`/`master`.

## Implementation Planning Flow

For non-trivial features, follow these phases strictly. Skipping leads to plans that fail audit.

### Phase 1: Fact Gathering

**Never write a plan from memory. Always verify against code first.**

Read all affected interfaces, entities, schemas, DB migrations, endpoints, and test patterns. Document findings in a **Fact Sheet** at the top of the plan. Every table, method signature, and constraint must be listed as verified facts before writing plan logic.

### Phase 2: Plan Writing

- **Use exact names from code** — copy method signatures, table/column names, type names from source
- **Every data operation** must reference real entities, real properties, real types
- **Every endpoint** must specify HTTP method, URL, request/response shape, permissions
- **Every new method** must show its full signature (or be marked NEW explicitly)

### Phase 3: Self-Audit Checklist

Before requesting review:

```
□ Data references: tables/entities/columns/types exist and match
□ Method calls: exact signatures exist (or marked NEW with full signature)
□ New methods: have happy/error/edge test cases in test plan
□ Cross-references: interface = usage, file list = all changed files, counts match
```

Extend with stack-specific checks from the project CLAUDE.md.

### Phase 4: Atomic Fixes

One change = check **all** references. Added a method → update: interface + test plan + file list. Changed a query → verify: constraints + types + related queries + cache invalidation.

---

# Obsidian — Project Documentation

> Projects may use Obsidian vaults for documentation. `docs/` may be a symlink to an Obsidian vault on Windows FS. See project CLAUDE.md for vault-specific structure.

## Frontmatter

Every `.md` file in `docs/` must have YAML frontmatter:

```yaml
---
created: YYYY-MM-DD        # required
updated: YYYY-MM-DD        # required, update on every change
type: plan|analysis|reference|legal  # required
status: draft|active|completed|archived  # required
area: scheduler | jira | persistence | infra   # required, see project CLAUDE.md for values
tags: [tag1, tag2]         # required, lowercase, use hyphens
---
```

Optional fields: `side` (backend|frontend), `phase`, `lang` — add as needed per project.

## Folder Organization

Keep `docs/` organized by category. Never put files in the root — always use a subfolder. Standard categories (create as needed per project):

| Folder | Content | Frontmatter type |
|---|---|---|
| `architecture/` | Architecture patterns, state management, design overviews | reference |
| `integrations/` | External service integrations, APIs, sync flows | reference |
| `infrastructure/` | Deployment, servers, configs, env vars, CI/CD infra | reference |
| `testing/` | Test architecture, patterns, CI/CD pipeline | reference |
| `analysis/` | Performance audits, tech debt, code analyses | analysis |
| `legal/` | Privacy policies, terms, cookies policies | legal |
| `plans/` | Feature implementation plans (`{feature}/be-plan.md` + `fe-plan.md`) | plan |

When creating a new file, pick the matching category folder. If none fits, check if a new category is warranted — prefer adding to an existing folder over creating one-off folders in the root.

## Rules

- **Always add frontmatter** to new `.md` files — Dataview queries depend on it
- **Update `updated` date** when modifying existing files
- **Keep docs in sync with code** — when your code changes affect information described in any `docs/` file, update the affected doc and set `updated` to today's date. Don't leave stale content
- **No files in docs/ root** — use category subfolders (see table above)
- **Flat file names** for plans: `be-plan.md` / `fe-plan.md` (no `be/plan.md` subfolders)
- **Non-markdown files** (configs, etc.) — keep as-is, create a companion `.md` with description
- **Don't touch** `.obsidian/`, `Templates/`, `Project Kanban.md` — Obsidian-managed

---

# Knowledge base — persistent across sessions

> Fill in the tool and its call names once, here. The rules below do not change with
> the tool; the call names do, and an agent given the rules without the calls will not
> use the base at all.
>
> | Placeholder | Your tool's call |
> |---|---|
> | ``mempalace_search`` | full-text or semantic search over stored findings |
> | ``mempalace_kg_query`` | relationship query: what depends on what |
> | ``mempalace_check_duplicate`` | duplicate check before a write |
> | ``mempalace_add_drawer`` | write one finding |
> | ``mempalace_update_drawer`` / ``mempalace_delete_drawer`` | supersede or remove a finding |
> | ``mempalace_kg_add`` / ``mempalace_kg_invalidate`` | temporal facts with a valid-from |
> | ``mempalace_diary_read`` / ``mempalace_add_drawer` into the diary partition` | session log |
> | ``mempalace mine`` | code index build, if the tool has one |
>
> Known implementations, and the tool decides the mode: **mempalace** is local, one store
> per developer — what you write, only you read. **wippy-kb** is global, one store for
> everyone — what you write, everyone reads, so a wrong fact is everyone's until it is
> invalidated. Whichever it is, state how storage is partitioned and which partitions the
> agent may write. If a code index is generated, the agent writes findings only, never
> the index.

## Rule 1: session start

When receiving a task:
- ``mempalace_diary_read`` — what happened in previous sessions
- ``mempalace_search`` — prior context related to the task: decisions, bugs, gotchas
- ``mempalace_kg_query`` — relationships of the entities and modules involved

## Rule 2: before changing code

Before modifying a module, service or feature:
- ``mempalace_search`` — prior decisions, bugs and workarounds in this area
- ``mempalace_kg_query`` — what depends on this module, and what it depends on

A miss is a result. Record it, so the next session does not repeat the search.

## Rule 3: after significant events

Record knowledge **not derivable from code alone**. Always ``mempalace_check_duplicate`` first.

| Event | Action | Where |
|---|---|---|
| Architecture or design decision | ``mempalace_add_drawer`` — decision + rationale + alternatives | area partition, or `decisions` |
| Bug found and fixed | ``mempalace_add_drawer`` — root cause + fix + prevention | `bugs` |
| Non-obvious behaviour or workaround | ``mempalace_add_drawer`` — what, why, where | area partition |
| Previous decision superseded | ``mempalace_update_drawer`` or ``mempalace_delete_drawer`` | — |

Content rules: concise and factual — what, why, where (file paths). No raw code dumps.
Use existing partitions; create a new one only when nothing matches.

### Temporal facts

Use ``mempalace_kg_add``, always with a valid-from date, for **any project fact that may
change over time**:

| Fact type | Example |
|---|---|
| Module dependency | `"OrderService" → "uses" → "CartService"` |
| Library or stack choice | `"CartModule" → "caches_with" → "Redis"` |
| Integration status | `"PaymentsIntegration" → "status" → "inactive"` |
| Feature state | `"WheelSearch" → "state" → "production"` |
| Architecture pattern | `"MediaUpload" → "uses" → "S3PreSignedUrls"` |

When a fact changes:
1. ``mempalace_kg_invalidate`` the old fact — sets an end date, preserves history
2. ``mempalace_kg_add`` the new fact with a valid-from
3. Never delete. An invalidated fact stays queryable, so a wrong earlier answer stays
   explainable.

Query with an as-of date to check what was true at a given point in time.

## Rule 4: before answering questions about past work

When the user asks about past decisions, events or context — ``mempalace_search`` or
``mempalace_kg_query`` first. Never answer from memory without verifying against the base.

## Rule 5: session end

After significant work, write a session entry — what was done, key decisions, open
questions.

> **Record the tool's quirks here, verbatim.** Example from one implementation: the
> diary-write call is not exposed to Claude Code's MCP client, because its root-level
> `anyOf` input schema is dropped during `tools/list`, so only diary-read appears. The
> workaround is to write the entry through the ordinary write call into the diary
> partition. Quirks like this are invisible from the outside and cost a session each
> time they are rediscovered.

## Search tips

- Always scope the search to the partition when you know the area — it improves precision
- Use ``mempalace_kg_query`` for temporal questions: "what changed in March?", "when was this
  decided?"
