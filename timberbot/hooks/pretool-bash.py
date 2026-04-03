"""PreToolUse hook for Bash calls in timberbot sessions.

Blocks common mistakes:
- python timberbot.py (should be timberbot.py directly)
- cd anywhere before timberbot.py
- python -c with timberbot logic
- timberbot.py buildings name:X (use find() for filtering)
"""
import json
import sys

inp = json.load(sys.stdin)
cmd = inp.get("tool_input", {}).get("command", "")

def deny(reason):
    print(json.dumps({"permissionDecision": "deny", "reason": reason}))
    sys.exit(0)

# block python prefix
if "python timberbot" in cmd or "python3 timberbot" in cmd:
    deny("timberbot.py is on PATH. Run directly: timberbot.py <command>. Never use python prefix.")

# block cd before timberbot
if "cd " in cmd and "timberbot" in cmd:
    deny("Never cd before running timberbot.py. It is on PATH. Run directly: timberbot.py <command>")

# block python -c with timberbot logic
if "python -c" in cmd or "python3 -c" in cmd:
    deny("Never use python -c for timberbot operations. Use timberbot.py commands directly.")

# block buildings name:X (common mistake -- use find())
if "timberbot.py buildings" in cmd and "name:" in cmd:
    deny("buildings() does not accept name: parameter. Use: timberbot.py find source:buildings name:<value>")

# block full paths to timberbot.py
if "Documents" in cmd and "timberbot.py" in cmd:
    deny("timberbot.py is on PATH. Run directly: timberbot.py <command>. Never use full paths.")

# allow everything else
print(json.dumps({}))
