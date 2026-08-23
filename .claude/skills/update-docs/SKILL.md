---
name: update-docs
description: Update Obsidian docs after code changes — find affected docs/ files, update content and frontmatter dates. Use proactively after significant code changes that affect documented architecture, integrations, infrastructure, caching, state management, or testing patterns.
user-invocable: true
---

# Update Obsidian Documentation

After significant code changes, check if any `docs/` files need updating. This skill ensures documentation stays in sync with code.

## When to trigger

Use this skill (or trigger it proactively) when code changes affect:
- Architecture patterns, module structure, cross-module dependencies
- State management, auth flow, offline-first patterns
- Cache strategy (TTLs, keys, invalidation)
- Integration flows (OAuth, sync pipeline, webhooks)
- Infrastructure (services, env vars, deployment)
- Testing patterns, CI/CD pipeline
- Order workflow or business rules documented in docs/

Do NOT trigger for routine changes (bug fixes, UI tweaks, new endpoints) unless they change documented architecture.

## Procedure

### Step 1: Identify what changed

Run `git diff --name-only HEAD~1` (or check the current session's changes) to see modified files. Summarize what was changed and why.

### Step 2: Find affected docs

Search `docs/` for files that describe the changed area:

```bash
# Search for keywords related to the change
grep -rl "keyword" docs/ --include="*.md"
```

Also check the mapping in CLAUDE.md ("Backend/Frontend Documentation Updates" tables) to find which docs should be updated for the type of change made.

### Step 3: Read and compare

For each potentially affected doc:
1. Read the doc file
2. Compare its content with the actual code changes
3. Identify stale, incorrect, or missing information

### Step 4: Update docs

For each doc that needs changes:

1. **Update the content** — fix stale info, add new info, remove obsolete sections
2. **Update frontmatter `updated` date** to today's date:
   ```yaml
   updated: YYYY-MM-DD  # today's date
   ```
3. **Keep docs concise** — don't add code descriptions that can be read from source. Only document architecture decisions, WHY rationale, data flows, and patterns not derivable from code

### Step 5: Report

List what was updated:
- Which docs files were changed
- What was updated in each
- Any docs that were checked but didn't need changes

## Rules

- **Never add code duplication** to docs — entity fields, endpoint lists, type definitions belong in code, not docs
- **Preserve doc style** — match the existing format, tone, and level of detail in each file
- **Follow folder organization** — new docs go in the right category subfolder (architecture/, integrations/, infrastructure/, testing/, analysis/, legal/, plans/). Never put files in docs/ root
- **Update, don't append** — if info changed, fix the existing section. Don't add "Update 2026-04-14: ..." notes
- **Delete stale content** — if a section is no longer accurate and can't be fixed, remove it rather than leaving it wrong
