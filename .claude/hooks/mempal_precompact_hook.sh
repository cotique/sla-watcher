#!/bin/bash
# MEMPALACE PRE-COMPACT HOOK — Emergency save before compaction
#
# Fires RIGHT BEFORE context compaction. First hit per window BLOCKS to force
# the AI to save all unarchived session content. Subsequent hits within the
# grace window APPROVE, so the human can retry /compact after the save.

GRACE_SECONDS=300   # 5 minutes between block + approve

STATE_DIR="$HOME/.mempalace/hook_state"
mkdir -p "$STATE_DIR"

INPUT=$(cat)

eval $(echo "$INPUT" | python3 -c "
import sys, json
data = json.load(sys.stdin)
sid = data.get('session_id', 'unknown')
cwd = data.get('cwd', '')
import re
safe = lambda s: re.sub(r'[^a-zA-Z0-9_/.\-~]', '', str(s))
print(f'SESSION_ID=\"{safe(sid)}\"')
print(f'CWD=\"{safe(cwd)}\"')
" 2>/dev/null)

MARKER="$STATE_DIR/${SESSION_ID}_precompact_last_fired"
NOW=$(date +%s)

# If marker exists and is fresh, this is a retry after the AI finished saving.
# Consume the marker and approve the compact.
if [ -f "$MARKER" ]; then
    LAST=$(cat "$MARKER" 2>/dev/null || echo 0)
    AGE=$((NOW - LAST))
    if [ "$AGE" -lt "$GRACE_SECONDS" ]; then
        echo "[$(date '+%H:%M:%S')] PRE-COMPACT approve (retry within ${AGE}s) for session $SESSION_ID" >> "$STATE_DIR/hook.log"
        rm -f "$MARKER"
        echo "{}"
        exit 0
    fi
    # Marker is stale, treat as first hit of a new window.
fi

# First hit: write marker, trigger mine, and block to force a save.
echo "$NOW" > "$MARKER"
echo "[$(date '+%H:%M:%S')] PRE-COMPACT block (first hit, marker set) for session $SESSION_ID" >> "$STATE_DIR/hook.log"

cat << 'HOOKJSON'
{
  "decision": "block",
  "reason": "COMPACTION IMMINENT (MemPalace). Save ALL session content before context is lost via MCP:\n1. mempalace_add_drawer (wing: wing_claude, room: diary) — thorough AAAK-compressed session summary. NOTE: mempalace_diary_write is unavailable in Claude Code's MCP client (its anyOf schema gets dropped), so write the diary as a drawer to wing_claude/diary instead.\n2. mempalace_add_drawer (project wing) — ALL verbatim quotes, decisions, code, context.\n3. mempalace_kg_add — entity relationships (optional).\nBe thorough — after compaction, detailed context will be lost. Do NOT write to Claude Code's native auto-memory (.md files). Save everything to MemPalace, then RETRY /compact — the second /compact within 5 minutes will be approved automatically."
}
HOOKJSON
