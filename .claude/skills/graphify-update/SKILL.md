---
name: graphify-update
description: Incrementally update graphify knowledge graphs after significant code/docs changes. Use proactively after creating new modules/entities, modifying architecture docs, or completing major refactors (>10 files). Only runs if the project has graphify set up (graphify-out/ exists).
user-invocable: true
---

# Graphify Update

Incrementally update graphify knowledge graphs when significant changes warrant it. This skill checks whether an update is actually needed before running anything.

## Prerequisites check

Before doing anything, verify that this project uses graphify:

```bash
# Check for graphify-out/ in current dir or immediate subdirectories
found=0
for d in . */; do
    if [ -d "${d}graphify-out" ] && [ -f "${d}graphify-out/graph.json" ]; then
        echo "graphify found: ${d}graphify-out/"
        found=1
    fi
done
if [ "$found" -eq 0 ]; then
    echo "NO_GRAPHIFY"
fi
```

If output is `NO_GRAPHIFY` — stop immediately. Say: "This project doesn't use graphify. Run `/graphify <path>` first to build a knowledge graph." Do NOT proceed.

## Step 1: Determine what changed

Check what files were modified since the last graphify run:

```bash
# Find all graphify-out directories with manifest.json
for gdir in $(find . -maxdepth 3 -name "manifest.json" -path "*/graphify-out/*" 2>/dev/null); do
    project_dir=$(dirname $(dirname "$gdir"))
    echo "=== $project_dir ==="
    
    # Get manifest timestamp
    last_run=$(stat -c %Y "$gdir" 2>/dev/null || stat -f %m "$gdir" 2>/dev/null)
    
    # Check if it's a git repo (look for .git in project_dir or parents)
    git_dir="$project_dir"
    while [ "$git_dir" != "/" ] && [ ! -d "$git_dir/.git" ]; do
        git_dir=$(dirname "$git_dir")
    done
    
    if [ -d "$git_dir/.git" ]; then
        cd "$git_dir"
        # Files changed since manifest was last modified
        # Source extensions and vendored directories are project-specific. Override with
        # the two env vars; the defaults are a starting point, not a claim about this repo.
        EXTS="${GRAPHIFY_EXTS:-php|ts|tsx|js|cs|py|go|md|rst}"
        SKIP="${GRAPHIFY_SKIP:-node_modules|vendor|graphify-out|\.next|/bin/|/obj/}"

        changed=$(find "$project_dir" -newer "$gdir" -type f 2>/dev/null \
            | grep -E "\.($EXTS)\$" \
            | grep -Ev "($SKIP)" \
            | head -50)
        if [ -n "$changed" ]; then
            echo "$changed" | head -30
            total=$(echo "$changed" | wc -l)
            echo "Total changed: $total files"
        else
            echo "No changes since last run"
        fi
        cd - > /dev/null
    fi
done
```

## Step 2: Decide whether to update

Analyze the changed files. Apply these rules strictly:

### MUST update (any one is sufficient):
- New module/entity directory created (new dir in `app/src/*/` or `src/entities/*/` or `src/features/*/`)
- Architecture docs modified (`docs/architecture/`, `docs/integrations/`, `docs/infrastructure/`, `**/docs/*.md`)
- New DDD domain service or entity class added
- Integration code changed (external service clients, OAuth flows, sync pipelines)
- More than 15 code files changed since last run

### MUST NOT update (stop here):
- Only test files changed
- Only styling/CSS/formatting changes
- Only bug fixes in existing code (no new classes/modules)
- Only config/env changes
- Less than 5 files changed AND none are docs/architecture
- Last graphify run was less than 1 hour ago

### Edge cases — ask the user:
- 5-15 files changed but no docs/architecture — mention what changed and ask
- Changes to shared abstractions (interfaces, base classes) — might warrant update

If the decision is MUST NOT — say: "No graphify update needed. Changes are [describe]. AST hooks will handle code-level updates on next commit." Then stop.

## Step 3: Run incremental update

For each subproject that needs updating, run `/graphify <path> --update`.

Important:
- Only update subprojects with actual changes — don't update all 4 if only be-app changed
- Use `--update` flag — this only re-extracts changed files, not the full corpus
- If multiple subprojects need updating, run them sequentially (not in parallel — avoids resource contention on WSL)

Example:
```
/graphify be-app --update
/graphify fe-app --update
```

## Step 4: Report

After update, briefly report:
- Which subprojects were updated
- How many new/changed nodes
- Any new god nodes or community changes
- Skip if no meaningful changes to report

## Rules

- **Never run full `/graphify <path>`** — only `--update`. Full rebuilds are expensive and should be explicit
- **Never update without checking** — always run Steps 1-2 first
- **Respect .graphifyignore** — `--update` uses it automatically, don't override
- **Don't update docs graph separately** — the docs/ vault is a symlink, update it from the root project level
- **Be honest about cost** — if the update will trigger many semantic subagents (>50 non-code files changed), warn the user about token cost before proceeding
- **One update per session max** — if this skill was already invoked in this session, don't run again unless the user explicitly asks
