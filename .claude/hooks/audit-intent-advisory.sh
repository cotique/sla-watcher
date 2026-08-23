#!/usr/bin/env bash
# UserPromptSubmit advisory hook. Detects plan-done / audit-intent phrases and
# injects a reminder to invoke Skill(audit) before reporting back.
# Does NOT block.
#
# Add locale-specific alternations to the regex below if the team works in
# another language.

set -u

input="$(cat)"

prompt="$(echo "$input" | jq -r '.prompt // ""')"
if [[ -z "$prompt" ]]; then
  exit 0
fi

prompt_lc="$(echo "$prompt" | tr '[:upper:]' '[:lower:]')"

# Plan-done / audit-intent patterns. Tight to avoid firing on every casual
# "done" or "check this".
if echo "$prompt_lc" | grep -qE \
  'implementation (done|complete|ready)|finished implementing|plan (done|complete|ready)|ready for review|ready for audit|ready to merge|audit (this|the implementation|please)|review the implementation|/audit'; then
  cat <<'EOF'
{"hookSpecificOutput":{"hookEventName":"UserPromptSubmit","additionalContext":"Audit reminder: invoke Skill(skill: \"audit\") before reporting done. It auto-scopes from git diff, loads .claude/audit-rules/<stack>.md addons, walks the 10-section checklist (completeness vs plan, architecture, code, placement, security, GDPR, performance, tests, DB/migrations, docs), fixes what is fixable, and lists what is not. Pass `all` to force every stack, or `<stack>` to scope manually."}}
EOF
fi

exit 0
