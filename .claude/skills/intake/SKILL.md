---
name: intake
description: Stand up the project layer in a repository — run the architect intake and write the rules, skills and planner into the repo. Use on a repo that has no `.claude/audit-rules/shared.md` yet, or when `install.sh` hands off after copying the templates. Activates on "run the intake", "init this repo", "set up audit rules", "the audit skill hard-stops".
---

# intake

Stands up the project layer. The generic layer is installed per user; **this** produces the
part that cannot be copied from anywhere — the rules that are facts about *this* codebase.

Without `shared.md` the `audit` skill hard-stops, so this runs before anything else is worth
installing. Do not aim for a complete file. Aim for a committed one.

## Step 0. Is it already done?

Check, in this order, and stop at the first hit:

1. `<repo>/.claude/audit-rules/shared.md` exists and contains no unfilled placeholder
   (`<...>`, `TODO`, `TBD`) → **report what is present and stop.** Do not re-run the intake,
   do not rewrite files, do not re-ask the architect. Round 2 is a separate session with a
   human, not a second run of this skill.
2. `shared.md` exists with unfilled placeholders → list them, offer to fill only those, and
   leave every answered rule untouched.
3. No `shared.md` → continue.

Never overwrite a file a human has edited. If the template layer is present but the filled
artefacts are missing, that is the normal starting state.

## Step 1. Put the template layer in place

Copy from the asset bundle into `<repo>/.claude/`, skipping anything already there:

```
.claude/audit-rules/ARCHITECT-INTAKE.md   the protocol, kept for round 2
.claude/audit-rules/shared.md.template
.claude/audit-rules/stack-addon.md.template
.claude/agents/planner.md.template
.claude/CLAUDE.md.template                from project-CLAUDE.md.template
```

**Templates stay in the repo next to the files they produced.** Do not delete them after
filling. When the bundle changes, the diff between template and instance is the only way to
see what is new.

## Step 2. Read the repo before asking anything

Everything here is answerable without the architect. Asking about it wastes the session and
costs credibility.

| Fact | How |
|---|---|
| Which stacks are present | file extensions, project and manifest files |
| Repo topology | is the workspace root a repo, or an aggregator over sibling repos |
| CI config, and which variable scope wins | the CI files themselves |
| Review status names | the tracker's workflow, not assumption |
| Logging patterns | grep the code. Do not assume `_logger` or `Log.Information` exist |
| Migrations present | migration directories or tooling config |
| Test runners per stack | test config files |

Bring the results to the session as a filled draft. The architect corrects a draft far faster
than they answer an empty questionnaire.

## Step 3. Pick the entry path

One question decides it: *can we point at things that have actually gone wrong in this
codebase?*

- **yes** → path A, the project's own history
- **no, but there is a reference project everyone copies** → path B, diff against it
- **no to both** → path C, predictions and design decisions

Mixed cases are normal and get split: a new service in an old monorepo is B or C for the
service and A for the shared layer around it. Two passes, separate files. Never average them.

The full question sets, ordering and timeboxes are in `ARCHITECT-INTAKE.md`. Follow it; do not
improvise a shorter version. It opens on failures and traps deliberately, because that is what
people can actually answer.

## Step 4. Ask about the knowledge base

Ask plainly: **is there a knowledge store on this project, and which one** — `wippy-kb`,
`mempalace`, `iknow`, something else, or none.

- **Named** → fill the call-name table at the top of the workspace `CLAUDE.md`, and the
  knowledge-store section of `shared.md`: how storage is partitioned, which partitions the
  agent may write, whether an indexer owns the code index.
- **None** → say so in the file explicitly. The rules that depend on a store are then dead
  weight and should be removed, not left looking active.

An agent given the rules without the call names will not use the base at all.

Then write `.mcp.json` from `.mcp.json.template`: the knowledge store just named, the tracker
(ask for its base URL — never guess it, and never carry another project's), and nothing else.
**Delete the servers this project does not use.** A server that fails to start is noise on
every session, and noise gets ignored along with the real failures. Secrets stay as `${ENV}`
references — this file is committed.

## Step 5. Write the files

| Artefact | Path | From |
|---|---|---|
| `shared.md` | `.claude/audit-rules/` | answers + step 2 draft |
| one addon per stack | `.claude/audit-rules/` | `stack-addon.md.template`, one per stack found in step 2 |
| `CLAUDE.md` | `.claude/` | `project-CLAUDE.md.template` + answers |
| `.mcp.json` | repo root | `.mcp.json.template`, only the servers this project uses |
| repo skills | `.claude/skills/` | the bundle's skill templates, tuned to this project |
| planner | `.claude/agents/<stack>-planner.md` | `planner.md.template`; `discovery` has nothing to hand off to without one |

Every rule carries: the checklist section it belongs to, whether it is mechanical or a
judgment call, and on path B its provenance — inherited, inherited-modified, or new.

**Unanswered stays unanswered.** Anything the architect did not answer is written as a marked
gap, never as a plausible guess. A gap is visible and gets filled later; an invented rule is
indistinguishable from a real one and will be enforced.

## Step 6. Acceptance bar

Round 1 is done when:

```
□ shared.md exists, so audit runs at all
□ every rule is tagged with a checklist section
□ every rule says whether it is mechanical or a judgment call
□ no client identity, deployment detail or PII contents in the file
□ one addon per stack found in step 2, even if short
□ the file is committed, with a named owner
```

Path A adds: every rule traces to something that actually happened, and every "ask <name>
first" dependency that surfaced is now written down. Path B adds: every inherited rule has an
explicit keep / drop / change decision.

## Step 7. Hand off

Report, in this order: what was written, what was detected without asking, what the architect
answered, and **what is still a gap**. Then state that round 2 — the architect reading the
draft and correcting it — is a separate session, and that this skill will not run again on
this repo.

## What this does not do

- It does not verify that the rules are correct. Only the next audit finding does that.
- It does not install the generic layer. That is `install.sh`.
- It does not run round 2, and it does not re-run round 1.
