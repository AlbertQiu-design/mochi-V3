#!/bin/zsh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
exec swift "$ROOT/Scripts/mochi-upsert-task.swift" "$@"
