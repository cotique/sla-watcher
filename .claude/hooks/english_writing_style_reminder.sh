#!/usr/bin/env bash
# Global PreToolUse hook. Injects a short reminder about the
# english-writing-style skill at the moment the agent is about to write
# or edit a file. Applies regardless of chat language.
#
# Wired in ~/.claude/settings.json under PreToolUse with matcher
# "Write|Edit|NotebookEdit".

set -u

msg='english-writing-style: code, comments, commit messages, PR descriptions, and technical docs default to English unless the user explicitly asks otherwise. English output is semi-casual: no formal C2 vocabulary ("utilize", "furthermore", "aforementioned", "leverage"), no literary flourishes, no em dashes. Full rules in ~/.claude/skills/english-writing-style/SKILL.md'

jq -n --arg msg "$msg" '{
  hookSpecificOutput: {
    hookEventName: "PreToolUse",
    additionalContext: $msg
  }
}'
