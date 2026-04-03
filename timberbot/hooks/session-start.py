"""SessionStart hook for timberbot claude sessions.

Injects skill + live colony state as additionalContext.
"""
import json
import os
import subprocess
import sys

def run(cmd, timeout=5):
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, shell=True)
        return r.returncode == 0, r.stdout.strip()
    except Exception as e:
        return False, str(e)

parts = []

# read skill (slim boot prompt)
mod_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
skill_path = os.path.join(mod_dir, "skill", "timberbot.md")
if os.path.exists(skill_path):
    with open(skill_path) as f:
        parts.append(f.read())

# try to get live colony state
ok, ping = run("timberbot.py ping")
if ok:
    ok2, brain = run("timberbot.py brain", timeout=10)
    if ok2 and brain:
        parts.append("## CURRENT COLONY STATE\n\n" + brain)
    else:
        parts.append("## COLONY STATE: game reachable but brain failed. Run `timberbot.py brain` manually.")
else:
    parts.append("## COLONY STATE: game not reachable. Run `timberbot.py ping` to check.")

output = {
    "hookSpecificOutput": {
        "hookEventName": "SessionStart",
        "additionalContext": "\n\n".join(parts)
    }
}
print(json.dumps(output))
