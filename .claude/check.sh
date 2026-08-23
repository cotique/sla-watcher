#!/usr/bin/env bash
# Two questions, one script.
#
#   ./.claude/check.sh              # both
#   ./.claude/check.sh --layer      # is the project layer complete?   <- run this in CI
#   ./.claude/check.sh --machine    # can THIS machine run it?         <- run after a clone
#
# The layer is committed, so a clone carries it. What a clone cannot carry is the
# tools, the tokens and the MCP servers — that is the machine half, and it is why a
# developer who just cloned does not run install.sh, they run this.
#
# Layer failures exit 1. Machine failures exit 1 only with --strict, because a
# developer mid-setup should get a list, not a wall. In CI the machine half is
# skipped entirely.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLAUDE="$ROOT/.claude"
RULES="$CLAUDE/audit-rules"

MODE="both"
STRICT=0
for a in "$@"; do
  case "$a" in
    --layer)   MODE="layer" ;;
    --machine) MODE="machine" ;;
    --strict)  STRICT=1 ;;
    -h|--help) sed -n '2,14p' "$0"; exit 0 ;;
    *) echo "unknown argument: $a" >&2; exit 2 ;;
  esac
done
[ -n "${CI:-}" ] && [ "$MODE" = "both" ] && MODE="layer"

FAIL=0
WARN=0
bad()  { printf '  FAIL  %s\n' "$1"; FAIL=$((FAIL+1)); }
warn() { printf '  warn  %s\n' "$1"; WARN=$((WARN+1)); }
ok()   { printf '  ok    %s\n' "$1"; }
head2(){ printf '\n== %s\n' "$1"; }

# Placeholders the templates leave behind. A file that still carries one was copied,
# not filled, and every rule in it is decoration.
has_placeholder() {
  grep -qE '<[a-z_][a-z0-9_ -]*>|\bTODO\b|\bTBD\b|\bFIXME\b' "$1" 2>/dev/null
}

# On PATH is not the same as working. Windows ships a python3 stub that exists,
# resolves, and then refuses to run — checking with `command -v` reports it present
# and every downstream check then fails for the wrong reason.
runs() { "$1" --version >/dev/null 2>&1; }

# ----------------------------------------------------------------- layer

check_layer() {
  head2 "Project layer"

  if [ ! -f "$RULES/shared.md" ]; then
    bad "audit-rules/shared.md is missing — the audit skill hard-stops without it"
    bad "run the intake before anything else"
    return
  fi
  ok "shared.md present"

  has_placeholder "$RULES/shared.md" \
    && bad "shared.md still has unfilled placeholders" \
    || ok "shared.md has no placeholders left"

  # One addon per stack actually present in the repo. The stack names mirror the
  # default table in the audit skill; a project that overrides that table renames
  # its addons to match, and this list with it.
  declare -A EXT2STACK=(
    [php]=php [ts]=ts [tsx]=ts [cs]=dotnet [go]=go [py]=python [lua]=lua [rs]=rust
  )
  declare -A SEEN=()
  while IFS= read -r f; do
    ext="${f##*.}"
    s="${EXT2STACK[$ext]:-}"
    [ -n "$s" ] && SEEN[$s]=1
  done < <(git -C "$ROOT" ls-files 2>/dev/null | grep -vE '(^|/)(vendor|node_modules)/')

  if [ "${#SEEN[@]}" -eq 0 ]; then
    warn "no known source extensions found — stack detection had nothing to go on"
  else
    for s in "${!SEEN[@]}"; do
      if [ -f "$RULES/$s.md" ]; then
        has_placeholder "$RULES/$s.md" \
          && bad "addon $s.md still has unfilled placeholders" \
          || ok "addon $s.md present"
      else
        bad "stack '$s' is in the repo but $s.md is missing — the audit skips it silently"
      fi
    done
  fi

  # A template left next to its filled file is fine and deliberate. A template with
  # no filled file next to it is an unfinished intake.
  for t in "$CLAUDE"/CLAUDE.md.template "$ROOT"/CLAUDE.md.template "$ROOT"/.mcp.json.template; do
    [ -f "$t" ] || continue
    filled="${t%.template}"
    rel="${filled#$ROOT/}"
    if [ ! -f "$filled" ]; then
      bad "${t#$ROOT/} was never filled in — no $rel"
    elif has_placeholder "$filled"; then
      bad "$rel still has placeholders — copied, not filled"
    else
      ok "$rel filled"
    fi
  done

  if [ -f "$ROOT/.mcp.json" ]; then
    if runs jq; then
      jq -e . "$ROOT/.mcp.json" >/dev/null 2>&1 \
        && ok ".mcp.json is valid JSON" \
        || bad ".mcp.json is not valid JSON"
    elif runs python3; then
      # stdin, not a path: on Git Bash a /c/... path never reaches a Windows python
      python3 -c "import json,sys;json.load(sys.stdin)" < "$ROOT/.mcp.json" 2>/dev/null \
        && ok ".mcp.json is valid JSON" \
        || bad ".mcp.json is not valid JSON"
    else
      warn "no working jq or python3 — .mcp.json was not validated"
    fi
    grep -q '"_README"\|"_note"' "$ROOT/.mcp.json" \
      && warn ".mcp.json still carries the template's _README/_note keys"
    has_placeholder "$ROOT/.mcp.json" \
      && bad ".mcp.json still has placeholders — a server that cannot start is noise every session"
  fi

  if [ -d "$CLAUDE/skills/discovery" ] && ! ls "$CLAUDE"/agents/*-planner.md >/dev/null 2>&1; then
    bad "discovery is installed but no planner exists — discovery has nothing to hand off to"
  fi

  for h in "$CLAUDE"/hooks/*.sh; do
    [ -f "$h" ] || continue
    [ -x "$h" ] || bad "hook $(basename "$h") is not executable"
  done
}

# --------------------------------------------------------------- machine

check_machine() {
  head2 "This machine"

  for t in git jq python3; do
    if runs "$t"; then
      ok "$t"
    elif command -v "$t" >/dev/null 2>&1; then
      warn "$t is on PATH but does not run — on Windows this is usually the Store stub"
    else
      warn "$t is not installed — the hooks need it"
    fi
  done

  # Every ${VAR} referenced by the committed MCP config has to exist here. This is
  # the single most common reason a freshly cloned repo behaves differently.
  if [ -f "$ROOT/.mcp.json" ]; then
    for v in $(grep -oE '\$\{[A-Z_][A-Z0-9_]*\}' "$ROOT/.mcp.json" | tr -d '${}' | sort -u); do
      if [ -n "${!v:-}" ]; then ok "\$$v is set"; else warn "\$$v is not set — the MCP server using it will fail to start"; fi
    done
  else
    warn ".mcp.json is missing, so no MCP servers are configured for this repo"
  fi

  [ -e "$ROOT/docs" ] || warn "docs/ is not present — symlink your vault if this project uses one"
  [ -f "$HOME/.claude/CLAUDE.md" ] || warn "no personal \$HOME/.claude/CLAUDE.md — yours, not the project's"
}

# ------------------------------------------------------------------ run

[ "$MODE" = "layer" ] || [ "$MODE" = "both" ] && check_layer
[ "$MODE" = "machine" ] || [ "$MODE" = "both" ] && check_machine

printf '\n'
if [ "$FAIL" -gt 0 ]; then
  printf '%s failure(s), %s warning(s)\n' "$FAIL" "$WARN"
  exit 1
fi
if [ "$WARN" -gt 0 ] && [ "$STRICT" = "1" ]; then
  printf '0 failures, %s warning(s) — failing on --strict\n' "$WARN"
  exit 1
fi
printf 'ok — %s warning(s)\n' "$WARN"
