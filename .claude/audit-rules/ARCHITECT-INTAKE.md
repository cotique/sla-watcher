# Architect intake — filling the audit rules for a project

The `audit` skill is generic. What makes it actually catch things is a project-specific
rules file, and that knowledge lives in one or two people's heads. Copying another team's
rules does not work — theirs are facts about their codebase. This is the protocol for
getting yours out of the architect and into a file.

**Who:** the architect or tech lead who reviews most of the code, plus whoever will
maintain the rules file afterwards.
**Output:** `shared.md` plus one addon per stack, in `<project>/.claude/audit-rules/`.

> Without a `shared.md` the audit skill hard-stops — it walks up from the git root looking
> for `.claude/audit-rules/` and refuses to run if it finds nothing. So the first pass
> matters more than making it complete.

---

# Step 0 — pick the entry path

Round 1 differs by situation, and running the wrong one wastes the session. Round 2 is
shared.

| | Situation | Where the rules come from | Round 1 |
|---|---|---|---|
| **A** | Existing project, nothing written down | Its own history | [Path A](#path-a) |
| **B** | New project with a reference project everyone is copying | The reference project's rules, diffed — extracted from it first if unwritten | [Path B](#path-b) |
| **C** | Genuinely new, no precedent | Predictions and design decisions | [Path C](#path-c) |

**How to pick.** Ask one question: *can we point at things that have actually gone wrong in
this codebase?* If yes, path A. If no, but there is an existing project people are treating
as the model, path B — whether or not any code or scaffold exists yet, and whether or not
that project's rules are written down. If no to both, path C.

**Mixed cases are normal and should be split.** A new service inside an old monorepo is
path B or C for the service and path A for the shared layer around it. Run them as two
passes and keep the results in separate files — `shared.md` from the mature side, the
stack addon from the new side. Do not average them into one vague document.

**Timebox:**

| Path | Round 1 | Round 2 |
|---|---|---|
| A | ~90 min, plus prep | ~45 min |
| B | ~60 min — plus a full path A against the reference project if its rules are unwritten | ~45 min |
| C | ~45 min | ~30 min |

---

<a name="path-a"></a>
# Round 1 — Path A: existing project, nothing written down

The most common case and the richest. History is the asset. The risk is archaeology:
knowledge is scattered, contradictory, and some of it left with people who are gone.

## Prep before the session (do not skip)

Arrive with a candidate list rather than a blank page. Two hours of digging makes the
session three times better, because the architect reacts to specifics instead of trying to
remember in the abstract.

- `git log` filtered to reverts and hotfixes — each one is a rule that did not exist
- The incident or postmortem log, if there is one
- Review comments on the last ~30 merged PRs — repetition is the signal
- CI failure history — which job fails most, and is it always the same cause

Bring the top 10 as a list. "I found nine reverts touching migrations in six months, what
is going on there" gets a better answer than "tell me about your migrations".

## Start with failures, not structure

These produce better rules than any architectural question, because a rule nobody has been
burned by is noise.

1. **What were the last five things that broke, got rolled back, or shipped wrong?**
   For each: what was the actual cause, and would a reviewer have caught it?
2. **What comment do you find yourself writing in review over and over?**
3. **What do you check in every review that no linter checks for you?**
4. **What has a new joiner broken in their first month?**
5. **Which part of the code does everyone route around?** What is the unwritten rule that
   keeps people out of it?
6. **Is there anything where the rule is "ask <name> before you touch it"?**
   That is an undocumented rule with a single point of failure, and it is the highest-value
   thing in the session.
7. **When did a green result last lie to you?** A suite that passed without running, a
   linter clean on code that was not compiled, a job that skipped its real work and exited
   zero. Ask it directly — people do not volunteer these, because a false green produces no
   incident until much later, and by then it gets filed under whatever broke downstream.
8. **Which of our checks would we notice had stopped running?** Anything that would go
   unnoticed is not a check, it is decoration. This usually finds more than question 7
   does, because it does not require anyone to remember being burned.

## Then the traps

9. **Is there a step that is silently required and easy to forget?** Schema regeneration
   after a model change, a second registration file, a codegen target, a cache to invalidate.
10. **Which config is not environment-interpolated**, so a local change means editing a
   literal and remembering to revert it before commit?
11. **Ports and versions:** which non-default ports do we use and why, and is any dependency
   pinned to an old version because a newer one breaks something?
12. **CI variable precedence:** which scope wins? Name the variables where this has actually
    caused a wrong-environment run.
13. **Destructive jobs:** does anything in the test suite drop or wipe data, and what stops
    it pointing at a real database?

## Then the boundaries

14. **What personal data does the system hold?** Field by field. Then: what must never reach
    a log?
15. **Where do secrets live**, and which services can rotate without downtime versus which
    need a maintenance window?
16. **Which env file templates are allowed to be committed**, and what rejects the others?
17. **Cross-repo contracts:** which pairs of things must change together, and what happens if
    only one ships?

## Then the mechanics

18. **Tests:** what is the real coverage situation? What is the safe subset to run locally?
    What is slow enough that nobody runs it?
19. **Pre-ship commands per stack**, exactly as typed — including any that do more than they
    look like they do.
20. **What must not be renamed** without a migration? Any old-versus-new naming still in
    flight, and which one new code uses?

## Then the one that catches the rest

21. **What would you tell someone in their first hour that is written down nowhere?**

Ask 21 last and expect the best answer of the session from it.

---

<a name="path-b"></a>
# Round 1 — Path B: new project modelled on an existing one

Path B is defined by **the existence of a reference project**, not by the existence of a
template repo. The common version of this case has no code and no scaffold at all — just
everyone agreeing that it will be "basically like X". That still counts, and it is still
the wrong moment to run a blank elicitation: the knowledge you need is in X, not in a
project that does not exist yet.

**Do not run path A against the new project.** It has no history to mine. Running it
produces invented answers.

## B0 — check the precondition first

Before booking the session, answer one question:

> **Does the reference project have written rules?**

| | Then |
|---|---|
| **Yes** | Go straight to the diff interview below. |
| **No** | **Run path A against the reference project first**, with *its* architect. Only then come back and diff. |

The second row is the common one and it is easy to get wrong. "Everyone knows it'll be like
X" usually means X's rules are tacit too — so there is nothing to inherit yet, and a session
that assumes otherwise produces a file full of half-remembered generalities. Extracting X's
rules is worth doing on its own merits regardless: X is a live project running without them.

Note this may mean a different architect in the room. Invite the person who owns the
reference project, not only the person who will own the new one.

## B1 — where it will *not* be a clone

Take this before touching the rule list. "Almost a clone" is agreed cheaply and the whole
risk lives in the "almost", so hunt the deltas explicitly:

1. **Walk the axes and mark same / different / unknown:** stack and framework versions,
   persistence, deployment target, scale and traffic shape, data classification and
   regulatory exposure, client constraints, integrations, team size and seniority mix,
   timeline pressure.
2. **What did we say would be the same, that you privately doubt?** Ask it exactly like
   that. It surfaces the deltas people are not arguing about yet, which are the ones that
   cost money later.
3. **Why are we building a new project instead of extending X?** The answer names the real
   difference, and it is often the one nobody listed under (1).
4. **Which `unknown` will we know first?** Those go on the verification list below rather
   than into a rule.

Deltas from (1) and (2) drive most of the drops and changes in the next step, so doing this
first makes the rule pass much faster.

## The three-way decision

For each inherited rule:

| Decision | Meaning |
|---|---|
| **Keep** | True here for the same reason it was true there |
| **Drop** | Not applicable — different stack, deployment target, data class, or it was never really true |
| **Change** | The concern applies but the specifics differ — rewrite it |

Nothing may stay undecided. An inherited rule nobody has looked at is worse than no rule,
because it carries false authority.

## Questions that drive the diff

1. **Which of these rules does a delta from B1 touch?** Work the deltas through the list
   first; they account for most of the drops and changes on their own.
2. **Which of the parent's rules were never actually true**, or stopped being true and
   nobody removed them? Cargo-culted rules propagate hardest, precisely because copying is
   easy.
3. **Which of the parent's rules exist to work around something we are not carrying over?**
   A pin, a legacy fork, a shared database. If the cause is gone, the rule goes.
4. **What did we regret on the parent project that we get to fix here?** These become rules
   in the new file that never existed in the old one, and they are usually the best rules in
   it.
5. **What is the parent project's rule we always argued about?** Either settle it here or
   mark it explicitly as a judgment call.
6. **What will be different in six months** that is not different yet? Do not write rules
   for it, but note it so the file is not surprised.

## Provenance

Mark each rule with where it came from: `[inherited]`, `[inherited, modified]`, `[new]`.

This matters more than it looks. In a year, when someone asks whether a rule is real or
just copied, provenance is the only thing that answers it — and unmarked inheritance is
exactly how the parent's dead rules ended up outliving their cause.

**When the new project has no code yet, add `unverified` to every inherited rule.** You are
predicting that a rule applies, not observing that it does, and the difference is worth
recording. Without the marker the file reads as if it were derived from this codebase, which
is precisely the false authority the provenance markers exist to prevent.

## Verification pass

Anything inherited into a project that does not exist yet is a hypothesis. Book a short
pass at the **first real milestone** — first service running end to end, or first
deployment, whichever comes first:

- Drop the `unverified` marker from every rule the code has now confirmed.
- Delete the ones the code has contradicted, and note what the delta actually turned out
  to be.
- Resolve the `unknown` axes from B1, which by then are mostly known.
- Add the rules that only became visible once real code existed. There will be some, and
  they are the first genuinely native rules the project has.

Put it in a calendar. Same failure mode as path C: the provisional file quietly becomes
permanent, and a year later nobody can tell which rules were ever checked.

## Then

Run path A questions 12–15 (boundaries) fresh. PII, secrets and cross-repo contracts are
the areas where "same as the parent" is most often assumed and most often wrong, and
getting them wrong is the expensive kind. This applies with more force, not less, when the
project is only an intention — "same data as X" is an assumption stated before anyone has
looked at the actual data.

---

<a name="path-c"></a>
# Round 1 — Path C: genuinely new

There is no history, so the failure-first questions have nothing to bite on. Asking them
anyway produces invented answers, which is worse than an empty file.

Switch from retrospective to prospective, and accept that the output will be short.

## Prospective questions

1. **What do you bet breaks first?** Not what could break — what you would put money on.
2. **What is the riskiest assumption we are building on?** What happens if it is wrong?
3. **What is irreversible here?** Data model, public API shape, auth model, anything with
   another team's client against it. Irreversible decisions deserve rules even with no
   history; reversible ones can wait for evidence.
4. **What would you not let someone merge unreviewed**, and why that specifically?
5. **What are we deliberately not doing** that people will be tempted to do? Standing
   out-of-scope items prevent a whole category of review argument.
6. **Which conventions are we choosing on purpose** rather than inheriting by default?
   Only these are worth writing down — defaults do not need a rule.

## Seed from decisions, not incidents

On a new project the design decisions are the only accumulated knowledge that exists. Every
ADR usually implies a rule:

> Decision: all inter-service calls go through the message bus, no direct HTTP.
> Rule: flag any direct HTTP client construction between our own services. [section 2]

Walk the ADRs and the architecture doc and convert each into a check. If there are no ADRs
yet, that is the finding — say so.

## Boundaries still apply

Run path A questions 12–15. PII and secrets rules are needed from day one, not once
something leaks. These are the one place a new project should have a complete answer, and
usually can.

## Set the growth mechanism explicitly

The file will be thin and that is correct. What matters is that it grows, and the only
habit that reliably does this:

> **When something breaks that a rule should have caught, the rule goes in the same PR as
> the fix.**

Write that line into the file itself. Then **schedule a real path A intake for 6–8 weeks
in**, once there is history to mine. Put it in a calendar, not a backlog. Path C is a
placeholder for path A, and the failure mode is the placeholder becoming permanent.

---

# Round 2 — after the architect has commented on the draft

Shared across all three paths. Write the draft from round 1, hand it over, then go through
their comments with these. This round is about turning statements into checks and cutting
what will not survive.

## Per rule they added or corrected

1. **Has this actually bitten someone, or does it just sound right?**
   If nobody can name an incident, cut it. Aspirational rules dilute the ones that matter.
   *Path C exception:* a rule guarding something irreversible stays even without an
   incident. Mark it `[predicted]` so it can be re-examined later.
2. **Is it a check or a fact?** "We use Postgres 15" is a fact and belongs in the project
   `CLAUDE.md`. "Migrations must be reversible or state why not" is a check and belongs
   here. The audit skill can only act on checks.
3. **Mechanical or judgment?** If a grep or a command can decide it, say so — the skill
   fixes those in place and marks them `FIXED`. If it needs a human call, it stays `OPEN`.
   Rules that do not declare which produce noisy audits.
4. **Which checklist section does it map to?** (2 architecture · 3 code and style ·
   4 placement and config · 5 security · 6 GDPR · 7 performance · 8 tests ·
   9 DB and migrations · 10 docs, CI and shipping). Untagged rules do not get routed.
5. **What is the counter-example?** When should this rule *not* be applied? A rule with no
   stated exception will be applied where it does not belong, and people will start ignoring
   the whole file.

## Across the whole draft

6. **What did you leave out because it felt too obvious?** This is where the highest-value
   items hide. Obvious-to-you is exactly the definition of the knowledge worth writing down.
7. **Which of these will be wrong in six months?** Mark them. A rule with a known expiry is
   more useful than one that quietly rots.
8. **Which of these must not leave the project?** Client identity, deployment detail,
   credential shapes, PII field lists. Rules stay, customer facts go — the file should be
   shareable with another team without a redaction pass.
9. **Which stack addon is now thin enough to be useless?** Better three real rules in one
   addon than twenty vague ones spread across five.

**Path B addition:** re-check every rule still marked `[inherited]` that nobody discussed in
either round. Silence is not agreement — it usually means nobody read it.

---

# Acceptance bar for round 1

Do not aim for complete. The bar differs by path.

**All paths:**

```
□ shared.md exists, so /audit runs at all
□ Every rule is tagged with a checklist section
□ Every rule says whether it is mechanical or a judgment call
□ No client identity, deployment detail or PII contents in the file
□ The file is committed, with a named owner
```

**Path A additionally:**

```
□ Every rule traces to something that actually happened
□ Every "ask <name> first" dependency surfaced in the session is now written down
□ One addon per stack in the repo, even if short
```

**Path B additionally:**

```
□ The reference project's rules exist in writing (extracted via path A if they didn't)
□ Every axis in the B1 delta hunt is marked same / different / unknown
□ Every inherited rule has an explicit keep / drop / change decision
□ Every rule carries provenance: inherited, inherited-modified, or new
□ If the new project has no code yet, inherited rules are marked unverified
   and a verification pass is booked for the first milestone
□ Boundaries (PII, secrets, cross-repo contracts) were re-derived, not assumed
```

**Path C additionally:**

```
□ Rules exist for everything irreversible
□ Boundaries are complete even though the rest is thin
□ The "rule ships with the fix" line is in the file
□ A path A intake is in the calendar for 6–8 weeks out
```

That last line in the shared block — committed, with a named owner — is the one that usually
gets skipped, and it is the one that matters most. This file accumulates into the densest
description of how a project actually works that anyone will write. Untracked, it is one
laptop away from gone; unowned, it silently stops matching the code and quietly starts
lying.

---

# Keeping it alive

The rules file decays the same way documentation does, so give it the same treatment:

- When an audit finding turns out to be wrong, fix the rule, not just the code.
- When something breaks that a rule should have caught, add the rule in the same PR as the
  fix. This is the single habit that keeps the file honest.
- Re-run round 2, questions 6 and 7 only, once a quarter.
- When a `[predicted]` rule survives a year with no incident behind it, either find the
  evidence or drop it.
