"""Smoke tests for all API endpoints + TOON CLI output.

Requires a running game with any save loaded.

Usage:
    python timberbot/script/test_endpoints.py
"""
import json
import subprocess
import sys

from timberbot import Timberbot


def check(name, ok, detail=""):
    status = "PASS" if ok else "FAIL"
    print(f"  {status}  {name}" + (f" -- {detail}" if detail else ""))
    return ok


def main():
    bot = Timberbot()
    if not bot.ping():
        print("error: game not reachable")
        sys.exit(1)

    passed = 0
    failed = 0

    print("\n=== API endpoints ===\n")

    tests = [
        ("summary", lambda: bot.summary(), lambda r: "time" in r),
        ("time", lambda: bot.time(), lambda r: "dayNumber" in r),
        ("weather", lambda: bot.weather(), lambda r: "cycle" in r),
        ("population", lambda: bot.population(), lambda r: isinstance(r, list)),
        ("resources", lambda: bot.resources(), lambda r: isinstance(r, dict)),
        ("districts", lambda: bot.districts(), lambda r: isinstance(r, list)),
        ("buildings", lambda: bot.buildings(), lambda r: isinstance(r, list) and len(r) > 0),
        ("trees", lambda: bot.trees(), lambda r: isinstance(r, list)),
        ("gatherables", lambda: bot.gatherables(), lambda r: isinstance(r, list)),
        ("beavers", lambda: bot.beavers(), lambda r: isinstance(r, list) and len(r) > 0),
        ("prefabs", lambda: bot.prefabs(), lambda r: isinstance(r, list) and len(r) > 0),
        ("speed", lambda: bot.speed(), lambda r: "speed" in r),
        ("map (size)", lambda: bot.map(), lambda r: "mapSize" in r),
        ("map (region)", lambda: bot.map(120, 130, 125, 135), lambda r: "tiles" in r),
        ("scan", lambda: bot.scan(120, 135, 5), lambda r: isinstance(r, dict) and "occupied" in r),
    ]

    for name, fn, validate in tests:
        result = fn()
        detail = f"{len(result)} items" if isinstance(result, list) else ""
        if check(name, validate(result), detail):
            passed += 1
        else:
            failed += 1

    print("\n=== beavers detail ===\n")

    b = bot.beavers()[0]
    for field in ["id", "name", "wellbeing", "needs", "anyCritical"]:
        if check(f"has {field}", field in b):
            passed += 1
        else:
            failed += 1

    needs = b.get("needs", {})
    if check("needs have points", all("points" in v for v in needs.values())):
        passed += 1
    else:
        failed += 1

    print("\n=== TOON CLI output ===\n")

    cli = "python timberbot/script/timberbot.py"
    toon_tests = [
        ("summary", "day:"),
        ("speed", "speed:"),
        ("buildings", "{"),  # tabular or structured
        ("beavers", "wellbeing"),
        ("prefabs", "name"),
    ]

    for method, expect in toon_tests:
        out = subprocess.run(f"{cli} {method}", capture_output=True, text=True,
                             shell=True, cwd="C:/code/timberborn").stdout
        is_toon = "json" not in out[:20].lower() and expect in out
        if check(f"{method} TOON output", is_toon, f"{len(out)} chars"):
            passed += 1
        else:
            failed += 1
            print(f"         first 100 chars: {out[:100]}")

    print(f"\n=== {passed} passed, {failed} failed ===\n")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
