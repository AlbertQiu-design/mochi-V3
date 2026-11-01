#!/bin/bash
INPUT=$(cat)
python3 - <<'PY' "$INPUT"
import json,sys,time
from pathlib import Path
raw=sys.argv[1]
try:
    data=json.loads(raw)
except Exception as e:
    data={"_parse_error":str(e),"_len":len(raw)}
keys=sorted(data.keys()) if isinstance(data,dict) else []
prompt=data.get("prompt") if isinstance(data,dict) else None
diag={
  "event": data.get("hook_event_name") if isinstance(data,dict) else None,
  "keys": keys,
  "had_prompt": isinstance(prompt,str) and bool(prompt.strip()),
  "prompt_type": type(prompt).__name__,
  "prompt_len": len(prompt) if isinstance(prompt,str) else None,
  "conversation_id": data.get("conversation_id") if isinstance(data,dict) else None,
  "updated": time.time(),
}
Path.home().joinpath(".cursor/mochi-island-last-hook.json").write_text(json.dumps(diag,ensure_ascii=False,indent=2))
# also forward to real CLI
import subprocess,os
cli="/Users/albertsmacbookpro/Projects/mochi-V2.0/island/MochiIsland.app/Contents/MacOS/MochiHookCLI"
p=subprocess.run([cli],input=raw.encode(),stdout=subprocess.PIPE)
sys.stdout.buffer.write(p.stdout)
sys.exit(p.returncode)
PY
