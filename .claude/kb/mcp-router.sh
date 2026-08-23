#!/usr/bin/env bash
# MemPalace MCP router.
#
# Claude Code launches one MCP server per session, with cwd = the project dir.
# Pointing every session at the single shared palace (~/.mempalace/palace) means
# concurrent sessions in different repos write the same ChromaDB/HNSW store at
# once -> lock contention, apparent hangs, and the occasional poisoned HNSW
# segment ("Error loading hnsw index").
#
# This wrapper picks a per-repo palace based on $PWD and execs the real server
# with --palace, so two repos never share a store. Passing --palace (not just
# the env var) also moves knowledge_graph.sqlite3 inside the palace dir, so the
# fact graph is isolated per repo too (see mcp_server.py _resolve_kg_path).
#
# Subprojects of one workspace deliberately map to the SAME palace -- they are
# one project and share memory, which is why each arm matches on a prefix.
# Unmatched dirs fall back to the shared palace, so nothing breaks.
#
# Add one case arm per workspace you want isolated.
set -euo pipefail

# Override MEMPALACE_PYTHON if the install lives somewhere else.
PYTHON="${MEMPALACE_PYTHON:-$HOME/.local/share/uv/tools/mempalace/bin/python}"
PALACES="$HOME/.mempalace/palaces"
cwd="$PWD"

case "$cwd" in
  # One arm per isolated workspace. The trailing * makes subprojects of the
  # same workspace share one palace, which is what you want.
  #
  #   "$HOME/www/myproject"*) palace="$PALACES/myproject" ;;
  #
  *) palace="$HOME/.mempalace/palace" ;;
esac

# Never write to stdout here: the MCP stdio protocol owns it. exec replaces
# this shell so the server inherits the original stdin/stdout/stderr intact.
exec "$PYTHON" -m mempalace.mcp_server --palace "$palace"
