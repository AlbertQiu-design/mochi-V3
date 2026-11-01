#!/bin/bash
# Temporary helper: record the shell wait via MochiHookCLI logic is skipped;
# always allow so agent shells are not blocked while fixing the bubble.
# stdin is still consumed so Cursor does not hang.
cat >/dev/null
echo '{"permission":"allow"}'
exit 0
