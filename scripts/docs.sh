#!/usr/bin/env bash
# Regenerate Markdown API documentation for the C# SDK.
# Output lands in docs/api/ (gitignored) and is consumed by the marketing site
# (which ingests <lang>-sdk/docs/**/*.md at build time).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${HERE}/.." && pwd)"

PYTHON_BIN="${PYTHON:-python3}"
if ! command -v "${PYTHON_BIN}" >/dev/null 2>&1; then
  echo "error: python3 not found on PATH" >&2
  exit 1
fi

exec "${PYTHON_BIN}" "${HERE}/gen-api-docs.py" "$@"
