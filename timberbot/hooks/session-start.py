"""SessionStart hook for timberbot agent sessions.

Runs at session start. Injects colony state and hard rules as additionalContext
so the agent wakes up already knowing the game state. Even haiku can't mess this up.
"""
import json
import subprocess
import sys

def run(cmd, timeout=5):
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, shell=True)
        return r.returncode == 0, r.stdout.strip()
    except Exception as e:
        return False, str(e)

rules = """## TIMBERBOT SESSION RULES (injected by hook -- you MUST follow these)

- timberbot.py is on PATH. Run it directly: `timberbot.py <command> key:value ...`
- NEVER use `python` prefix. NEVER `cd` anywhere. NEVER use full paths.
- NEVER run mutating calls in parallel. Each changes state the next depends on.
- ALWAYS use find_placement before placing buildings. NEVER guess coordinates.
- ALWAYS run `timberbot.py prefabs | grep -i <keyword>` before placing a building you haven't placed this session.
- Prefabs require faction suffix (e.g. LumberjackFlag.Folktails, NOT LumberjackFlag).
- ALWAYS unpause the game (set_speed 1-3) after planning. Speed 0 = nothing happens.
- The game clock is always running. Idle time wastes food, water, and construction progress.
"""

parts = [rules]

# try to get live colony state
ok, ping = run("timberbot.py ping")
if ok:
    ok2, brain = run("timberbot.py brain", timeout=10)
    if ok2 and brain:
        parts.append("## CURRENT COLONY STATE (live from game)\n\n" + brain)
    else:
        parts.append("## COLONY STATE: game reachable but brain failed. Run `timberbot.py brain` manually.")
else:
    parts.append("## COLONY STATE: game not reachable yet. Run `timberbot.py ping` to check, then `timberbot.py brain goal:\"<goal>\"` when ready.")

output = {
    "hookSpecificOutput": {
        "hookEventName": "SessionStart",
        "additionalContext": "\n\n".join(parts)
    }
}
print(json.dumps(output))
