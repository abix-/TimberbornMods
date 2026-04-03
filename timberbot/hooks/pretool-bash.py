"""PreToolUse hook for Bash calls in timberbot sessions.

Blocks only high-signal Timberbot misuse:
- python -c timberbot hacks
- buildings name:X misuse (use find() instead)
- chained mutating timberbot commands
"""
import json
import re
import sys

inp = json.load(sys.stdin)
cmd = inp.get("tool_input", {}).get("command", "")
cmd_l = cmd.lower()


def deny(reason):
    print(json.dumps({"permissionDecision": "deny", "reason": reason}))
    sys.exit(0)


if ("python -c" in cmd_l or "python3 -c" in cmd_l) and "timberbot" in cmd_l:
    deny("Never use python -c for Timberbot operations. Use timberbot.py commands directly.")

if "timberbot.py buildings" in cmd_l and "name:" in cmd_l:
    deny("buildings() does not accept name:. Use: timberbot.py find source:buildings name:<value>")

mutating = re.findall(
    r"timberbot\.py\s+(place_[a-z_]+|demolish_[a-z_]+|set_[a-z_]+|plant_[a-z_]+|mark_[a-z_]+)",
    cmd_l,
)
if len(mutating) > 1 or (mutating and any(sep in cmd for sep in ("&&", "||", ";", "\n"))):
    deny("Run Timberbot mutating commands one at a time. Re-read state between mutation steps.")

print(json.dumps({}))
