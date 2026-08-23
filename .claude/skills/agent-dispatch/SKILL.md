---
name: agent-dispatch
description: Enforces efficient agent work distribution rules when spawning multiple subagents for multi-step tasks — audit before dispatch, wave-based execution, verification between waves, context economy
user-invocable: false
---

# Agent Dispatch Protocol

When dispatching work to subagents (Agent tool), follow this protocol strictly.
These rules optimize for token economy and reliability.

## Pre-Dispatch: Audit

Before spawning any agents, ALWAYS:

1. **Audit current state** — read target files, run tests, check what already exists
2. **Create work orders only for missing work** — never redo what's already done
3. **Evaluate scope** — if total tasks <= 5 across all files, implement directly without agents. Single file changes — always use Edit/Write directly. Research-only tasks — use Explore agent or Grep/Glob directly

## Pre-Filtering (orchestrator responsibility)

Before dispatching agents that process source documents (audit files, review reports, plans):

1. **Read the source file yourself** before creating agent prompts
2. **Extract only actionable findings** — exclude observations, positive findings, items marked "acceptable", "informational", "by design", "OK"
3. **Send the filtered list** to the agent, not the raw file path. This prevents agents from wasting tokens re-reading and filtering
4. **Deduplicate across agents** — if multiple source files reference the same file/finding, assign to ONE agent only. Prefer the most specific source (e.g., SECURITY audit over CODE-REVIEW for auth issues)

## Work Order Rules

Each agent receives a **minimal, explicit** work order:

- **One deliverable per agent** — a clear, bounded goal (e.g. "backend for feature X"), not "implement everything"
- **Max 5-7 files per agent** — if a task requires more files, split into multiple agents. Prefer 1 file per agent when possible
- **Max 30 check-items per agent** — if source has >30 actionable items, split by priority/category or instruct agent to check RED/YELLOW first and sample GREEN
- **Max 10-15 action items per agent** — small tasks finish faster and consume fewer tokens
- **Explicit task names** — e.g. `CreateUserService`, `TestAuth/token_expired`. Never vague descriptions
- **Minimal context with pattern reference** — provide only: target file path, task list, and ONE existing file of the same kind as a pattern (e.g. an existing controller as a pattern for a new controller). Tell agent which file to read before modifying. Do NOT copy entire plans, unrelated code, or read all files of the same type

## Output Economy

Instruct agents to minimize output tokens:

- **Structured table only, no explanatory prose** — each row: finding | file:line | status | 1-line remainder
- **Summary line at end:** `Total: X. Done: Y. Not done: Z. Partial: W.`
- **Do not repeat finding descriptions verbatim** from source — use short paraphrases
- **Do not list positive/unchanged items** unless explicitly asked — only report deviations (not done, partial)

## Read-Only vs Write Agents

- **Read-only agents** (search, verify, audit, explore): max 5 parallel, no wave barriers needed between them
- **Write agents** (edit, create, refactor): max 2-3 parallel, wave barriers mandatory

## Wave Execution (write agents)

Write agents are dispatched in waves:

- **Max 2-3 parallel agents per wave**
- **Sequential waves** — wave N+1 starts only after wave N completes AND is verified
- **Verification between waves:**
  1. Review each agent's output for correctness
  2. Run tests, check compilation, confirm no regressions
  3. Only then proceed to next wave

## Conflict Avoidance

- **Shared files = single writer** — files touched by multiple features (e.g. route registrations, shared configs, barrel exports) must be modified by ONE agent only, or sequentially across waves
- When in doubt, combine shared-file edits into one agent rather than risking merge conflicts

## Agent Lifecycle

- **Never recreate agents** — if an agent is blocked on permissions, resume it (SendMessage) instead of spawning a new one
- **Never use Haiku model** — results are too weak for coding tasks and require rework. Use Sonnet minimum, Opus for complex logic
- **On agent failure** — review the error, fix the root cause manually or in the current context, then resume the agent. Do not silently retry or pass broken state to the next wave. If the failure is unclear, report to the user before proceeding
