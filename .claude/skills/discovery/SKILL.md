---
name: discovery
description: "Pre-plan discovery interview. Takes a vague feature idea and turns it into a complete spec.md ready for handoff to a planner agent. Uses AskUserQuestion-driven categories (problem, UX, data, tech, scale, integrations, security, ops), spawns research loops on uncertainty, surfaces requirement conflicts, runs a completeness check before writing. Project-agnostic; loads per-project addons from .claude/discovery-rules.md. After the spec is done, can hand off to be-planner / fe-planner agents."
allowed-tools:
  - AskUserQuestion
  - Read
  - Bash
  - Grep
  - Glob
  - Write
  - WebFetch
  - WebSearch
  - Agent
---

# discovery

Run this BEFORE plan writing when the input is a vague idea (stakeholder request, brief sentence, half-formed thought). Turns it into a structured `spec.md` that `be-planner` / `fe-planner` (or equivalent planning agent) can consume.

Do NOT run for:
- Bugfixes (the bug is the spec).
- Small CRUD additions where the user already wrote a clear description.
- Single-file refactors.

Run for:
- New user-facing feature ("we want reviews", "add a wishlist").
- Cross-stack changes with non-obvious boundaries.
- Anything the user described in 1-2 sentences but is clearly bigger.

## Core philosophy

- Don't ask obvious questions.
- Don't accept surface answers.
- Don't assume knowledge on the user's side.
- Read code before recommending; spawn research when uncertain.
- A spec written in 3 questions is slop. Minimum floor is scope-adaptive (see Phase 5).

## Setup

### Locate the project root and rules root

```bash
ROOT="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
dir="$ROOT"
RULES_ROOT=""
while [ "$dir" != "/" ] && [ -n "$dir" ]; do
    if [ -f "$dir/.claude/discovery-rules.md" ]; then
        RULES_ROOT="$dir"
        break
    fi
    dir="$(dirname "$dir")"
done
```

`$ROOT` is where the spec will be written. `$RULES_ROOT/.claude/discovery-rules.md` (if found) contains project-specific question banks that specialize the generic categories below. If no addon exists, run with generic categories only.

If `$RULES_ROOT` is set, read its `discovery-rules.md` BEFORE starting the interview. Hold those bullets as inserts for the relevant categories.

## Phase 1: Orientation (2-3 questions)

Goal: understand the SHAPE of the idea before deep-diving.

Use `AskUserQuestion` for things like:
- "In one sentence, what problem are you trying to solve?"
- "Who will use this? (end users, sellers, admins, internal team, ML pipeline...)"
- "Is this a new thing or improvement to something existing? If existing, what's the rough surface?"

From the answers, classify the project shape:

| Shape                  | Focus areas                                     |
|------------------------|-------------------------------------------------|
| New user feature       | UX journey, data model, region/locale, GDPR     |
| Backend-only service   | data, scaling, integrations, idempotency        |
| Frontend-only change   | UX, state, SSR/CSR boundaries                   |
| Refactor               | scope boundaries, rollback path, test coverage  |
| Integration            | third-party API, auth, rate limits, fallbacks   |
| Job / cron / pipeline  | triggers, idempotency, retries, monitoring      |
| Infra change           | deployment, rollback, blast radius              |

## Phase 2: Category-by-category deep dive

Walk through the categories below IN ORDER. Skip categories that are clearly N/A for the detected shape (note the skip in the spec's "Out of Scope" later).

For every category:
1. Ask 2-4 questions via `AskUserQuestion`.
2. Detect uncertainty signals (see Phase 4).
3. If `discovery-rules.md` addon has a bullet for this category, fold it into the question wording.
4. Track decisions internally.

### A. Problem & Goals
- What's the current pain point? How do people solve it today?
- What does success look like? Measurable how?
- Who are the stakeholders beyond end users? (legal, finance, partners)
- What happens if this doesn't get built?

### B. User Experience & Journey
- Walk me through: a user opens this for the first time. What do they see? What do they do?
- What's the ONE thing a user MUST be able to do?
- What errors can happen? What should the user see when things go wrong?
- How technical / domain-savvy are these users?

### C. Data & State
- What needs to be stored? Temporarily or permanently?
- Where does data come from? Where does it go?
- Who owns the data? Are there privacy or compliance concerns?
- What happens to existing data if requirements change?

### D. Technical Landscape
- What existing systems does this need to work with?
- Are there technology constraints? (language, framework, deployment platform)
- What's the team's expertise around this surface?
- Anything that's off-limits to change?

### E. Scale & Performance
- How many users / requests are expected? (now vs in 6-12 months)
- What response times are acceptable?
- What happens during traffic spikes? Cache-friendly or write-heavy?
- Any known hot paths in the project this could intersect with?

### F. Integrations & Dependencies
- What external services does this need to talk to?
- What APIs need to be consumed? Created?
- Third-party dependencies — what's the fallback if they fail?
- Authentication / authorization story for those integrations?

### G. Security & Access Control
- Who should be able to do what?
- What data is sensitive? PII? Financial? Health?
- Compliance constraints? (GDPR, HIPAA, SOC2, regional rules)
- Auth: existing system, or new flow?

### H. Deployment & Operations
- How will this be deployed? By whom?
- What monitoring / alerting do we need?
- Updates / rollback story?
- Disaster recovery if relevant?

## Phase 3: Research loops

When you detect uncertainty, surface it explicitly:

```
AskUserQuestion(
  question: "You mentioned <topic>. There are several approaches with different tradeoffs. Want me to research before continuing?",
  options: [
    { label: "Yes, research it",     description: "I'll investigate options and explain tradeoffs" },
    { label: "No, I know what I want", description: "Skip research" },
    { label: "Brief overview",        description: "Quick summary without deep research" }
  ]
)
```

If research is approved:
1. Spawn an `Agent` (Explore / general-purpose) or use WebSearch / WebFetch.
2. Gather information.
3. Summarize in plain language.
4. Come back with INFORMED follow-up questions, not raw research output.

## Phase 4: Conflict resolution

Watch for and surface:
- "Simple AND feature-rich"
- "Real-time AND cheap infrastructure"
- "Highly secure AND frictionless UX"
- "Flexible AND performant"
- "Fast to ship AND future-proof"

When a conflict is detected:

```
AskUserQuestion(
  question: "There's a conflict: you want <X> but also <Y>. These usually don't combine because <reason>. Which is more important?",
  options: [
    { label: "Prioritize X", description: "<what we lose>" },
    { label: "Prioritize Y", description: "<what we lose>" },
    { label: "Explore alternatives", description: "Research ways to get both" }
  ]
)
```

## Phase 5: Completeness check (scope-adaptive floor)

Pick a floor based on the detected shape:
- Refactor / single-stack change / cron job: minimum 6 questions across at least 4 categories.
- Cross-stack feature / integration: minimum 10 questions, at least 1 research loop.
- New user-facing feature with backend + frontend + data: minimum 15 questions, at least 2 research loops.

Before writing the spec, verify:

```
Problem
  [ ] Clear problem statement
  [ ] Success metric stated
  [ ] Stakeholders identified

UX
  [ ] User journey mapped
  [ ] Core action defined
  [ ] Error / edge cases acknowledged

Tech
  [ ] Data model rough shape
  [ ] Integrations listed
  [ ] Scale expectation set
  [ ] Security boundary defined
  [ ] Deployment shape chosen

Decisions
  [ ] All tradeoffs explicitly chosen
  [ ] No "TBD" items remaining
  [ ] User confirmed the summary
```

If any box is empty, GO BACK and ask more questions before proceeding.

## Phase 6: Confirm the summary

Before writing the spec, paste a one-screen summary back to the user:

```
Quick check before I write the spec:

You're building <X> for <users> to solve <problem>.
Core experience: <journey>.
Key decisions:
- <Decision 1> (because <rationale>)
- <Decision 2> (because <rationale>)
- <Decision 3> (because <rationale>)
Out of scope: <list>.

Right?
```

`AskUserQuestion` with:
- "Looks right, write the spec"
- "Adjust some things first" (then loop back to relevant phases)
- "Discard and start over"

## Phase 7: Spec generation

Write to `$ROOT/docs/plans/<feature-slug>/spec.md` by default. If the project uses a different plans directory, the discovery-rules addon should override this path with a `plans_path:` field.

If the user didn't supply a `<feature-slug>`, ask. Don't invent it.

Spec template:

```markdown
---
created: <YYYY-MM-DD>
updated: <YYYY-MM-DD>
type: spec
status: draft
area: <project-specific value>
tags: [discovery, <relevant-tags>]
---

# <Feature name>

## Executive Summary
<2-3 sentences: what, for whom, why now>

## Problem Statement
<the problem, current pain points, why now>

## Success Criteria
<measurable outcomes>

## User Personas
<who uses this, their technical level, their goals>

## User Journey
<step-by-step flow of the core experience>

## Functional Requirements
### Must Have (P0)
- <requirement with acceptance criteria>

### Should Have (P1)
- <...>

### Nice to Have (P2)
- <...>

## Technical Architecture
### Data Model
<key entities and relationships, no field-level detail — that's the planner's job>

### System Components
<major components and responsibilities>

### Integrations
<external systems we connect to>

### Security Model
<auth, authorization, data protection>

## Non-Functional Requirements
- Performance: <metrics>
- Scalability: <expected load>
- Reliability: <uptime>
- Security: <compliance, encryption>

## Out of Scope
<explicitly NOT building>

## Open Questions for Implementation
<technical details to resolve during planning, not discovery>

## Appendix: Research Findings
<summaries of research conducted during discovery>
```

The spec is the input to the next step. It is NOT a plan. It does not contain method signatures, table column names, file paths, or test plans — those belong to the planner.

## Phase 8: Implementation handoff

After the spec is written, ask:

```
AskUserQuestion(
  question: "Spec saved at docs/plans/<feature-slug>/spec.md. Next step?",
  options: [
    { label: "Run planner agents now",  description: "Spawn be-planner / fe-planner with this spec as input" },
    { label: "Review the spec first",   description: "I'll read it and come back when ready" },
    { label: "Plan it manually",        description: "I'll write the plan myself, no agent" },
    { label: "Done for now",            description: "Save the spec, plan later" }
  ]
)
```

If "Run planner agents now":
1. Detect which stacks the spec touches (BE / FE / both / other). Cross-check against the technical-landscape answers from Phase 2.
2. Spawn the relevant planner via `Agent`:
   - Backend: `subagent_type: be-planner`, prompt includes the path to `spec.md` and the feature slug.
   - Frontend: `subagent_type: fe-planner`, same shape.
   - Other stacks: pick the closest planner agent the project has, or note that no planner exists and stop.
3. If both planners exist and both stacks are touched, spawn them in parallel (one Agent message with both calls).
4. After planners return, the spec lifecycle is done. The user can proceed to implementation.

If "Plan it manually" or "Review the spec first" or "Done for now":
- Note in chat where the spec lives so the user can pick it up later.

## AskUserQuestion best practices

Question phrasing:
- BAD: "What database do you want?" (assumes domain knowledge)
- GOOD: "What kind of data will you store, and how often will it be read vs written?"

Option design (always include uncertainty escape hatches):
```
options: [
  { label: "Option A",       description: "Clear choice with implications" },
  { label: "Option B",       description: "Alternative with different tradeoffs" },
  { label: "I'm not sure",   description: "Let's explore this more" },
  { label: "Research this",  description: "I'll investigate and come back" }
]
```

For requirement enumeration use `multiSelect: true`.

## Knowledge-gap detection signals

| Signal                                  | Action                                  |
|-----------------------------------------|-----------------------------------------|
| "I think..." / "Maybe..."                | Probe deeper, offer research            |
| "That sounds good" (to your suggestion)  | Verify they understand the implications |
| "Just simple/basic X"                    | Challenge — define what simple means    |
| Technology buzzwords without context     | Ask what they think it does             |
| Conflicting requirements                 | Surface the conflict (Phase 4)          |
| "Whatever is standard"                   | Explain there is no universal standard  |
| Short answers / long pauses              | They might be overwhelmed; simplify     |

## Iteration rules

1. Never write the spec after only 2-3 questions.
2. At least 2 questions per relevant category.
3. At least 1 research loop for any non-trivial project.
4. Always run the completeness check before writing.
5. Always summarize understanding before finalizing.
6. The spec output path is determined by `<ROOT>/docs/plans/<feature-slug>/spec.md` unless overridden by `discovery-rules.md`.
