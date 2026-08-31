#!/usr/bin/env bash
# Code-style checks that a compiler and an analyser do not make.
#
# Only rules that are checkable without an opinion live here. The ones that need a human to
# weigh context stay in .claude/audit-rules/shared.md and are raised by the audit instead.
#
# Documentation comments are deliberately not checked: their formatting is the author's, and a
# script that rewrites prose is a script that loses meaning.
#
# exit 0: clean    exit 1: at least one violation

set -u
cd "$(dirname "$0")/.." || exit 1

fail=0

report() {  # $1 = rule, $2 = what was found
  fail=1
  echo "  FAIL  $1"
  printf '%s\n' "$2" | sed 's/^/        /'
}

pass() {  # $1 = rule
  echo "  ok    $1"
}

echo "== Code style"

# ---------------------------------------------------------------------------------------
# An immutable string is built in one expression, not concatenated from fragments.
# A line that ends in `" +` or begins with `+ "` is a value assembled in pieces.
hits=$(grep -rn --include='*.cs' -E '("\s*\+\s*$)|(^\s*\+\s*")' src tests 2>/dev/null | grep -v '^\s*//')
if [ -n "$hits" ]; then
  report 'strings are built in one expression' "$hits"
else
  pass 'strings are built in one expression'
fi

# ---------------------------------------------------------------------------------------
# File-scoped namespace comes first, usings after it. Not the order the templates generate,
# so it is undone by anyone who lets the IDE reformat.
wrong=""
for file in $(git ls-files '*.cs' 2>/dev/null); do
  # A file of top-level statements has no namespace at all, so the rule cannot apply to it.
  grep -q -E '^\s*namespace\b' "$file" 2>/dev/null || continue
  first=$(grep -m1 -E '^\s*(namespace|using)\b' "$file" 2>/dev/null)
  case "$first" in
    using*) wrong="${wrong}${file}: starts with '${first}'
" ;;
  esac
done
if [ -n "$wrong" ]; then
  report 'namespace precedes usings' "$wrong"
else
  pass 'namespace precedes usings'
fi

# ---------------------------------------------------------------------------------------
# Every tracked file is English. Checked by codepoint rather than with `grep -P`, which
# reported zero matches on a file full of Cyrillic because this locale refuses the flag:
# "grep: -P supports only unibyte and UTF-8 locales".
runs() { "$1" --version >/dev/null 2>&1; }
python=""
for candidate in python3 python; do
  if runs "$candidate"; then python="$candidate"; break; fi
done

if [ -z "$python" ]; then
  echo "  skip  files are English (no working python on PATH, and grep -P is unreliable here)"
else
  cyrillic=$("$python" - <<'PY'
import io, subprocess, sys
files = subprocess.run(["git", "ls-files"], capture_output=True, text=True).stdout.split()
found = []
for name in files:
    try:
        text = io.open(name, encoding="utf-8").read()
    except (UnicodeDecodeError, OSError):
        continue
    # Codepoints, not letters: spelling the bounds out in Cyrillic would make this script
    # fail its own check.
    count = sum(1 for ch in text if 0x0400 <= ord(ch) <= 0x04FF)
    if count:
        found.append(f"{name}: {count} Cyrillic character(s)")
print("\n".join(found))
PY
)
  if [ -n "$cyrillic" ]; then
    report 'files are English' "$cyrillic"
  else
    pass 'files are English'
  fi
fi

echo
if [ "$fail" -eq 0 ]; then
  echo "ok"
else
  echo "style violations above"
fi
exit "$fail"
