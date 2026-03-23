"""Comprehensive endpoint tests with debug verification.

Tests every API endpoint, then uses the debug endpoint to verify
the game state actually changed. Requires a running game with
the Iron Teeth day-5 save.

Usage:
    python timberbot/script/test_validation.py
"""
import json
import sys
import time

from timberbot import Timberbot


class TestRunner:
    def __init__(self):
        self.bot = Timberbot()
        self.passed = 0
        self.failed = 0
        self.skipped = 0

    def check(self, name, ok, detail=""):
        if ok:
            self.passed += 1
            print(f"  PASS  {name}")
        else:
            self.failed += 1
            print(f"  FAIL  {name}")
            if detail:
                print(f"         {detail}")

    def skip(self, name, reason=""):
        self.skipped += 1
        print(f"  SKIP  {name}" + (f" ({reason})" if reason else ""))

    def has(self, result, key):
        """check result dict has key"""
        return isinstance(result, dict) and key in result

    def err(self, result):
        """check result is an error"""
        return isinstance(result, dict) and "error" in result

    def debug_get(self, path):
        """get a value from game internals via debug endpoint"""
        return self.bot.debug(target="get", path=path)

    def debug_call(self, path, method, **kwargs):
        """call a method on a game object via debug endpoint"""
        args = {"target": "call", "path": path, "method": method}
        args.update(kwargs)
        return self.bot.debug(**args)

    def find_building(self, name):
        """find first building matching name, return id"""
        buildings = self.bot._get("/api/buildings")
        if isinstance(buildings, list):
            for b in buildings:
                if name.lower() in str(b.get("name", "")).lower():
                    return b.get("id")
        return None

    def run(self):
        if not self.bot.ping():
            print("error: game not reachable")
            sys.exit(1)

        self.test_read_endpoints()
        self.test_speed()
        self.test_placement_and_demolish()
        self.test_priority()
        self.test_workers()
        self.test_pause()
        self.test_crops()
        self.test_tree_marking()
        self.test_stockpile()
        self.test_orientation()
        self.test_find_placement()
        self.test_summary_projection()
        self.test_map_moisture()
        self.test_unlock()

        summary = f"\n=== {self.passed} passed, {self.failed} failed"
        if self.skipped:
            summary += f", {self.skipped} skipped"
        summary += " ===\n"
        print(summary)
        sys.exit(1 if self.failed else 0)

    def test_read_endpoints(self):
        print("\n=== read endpoints ===\n")

        # each read endpoint should return data without error
        reads = [
            ("ping", lambda: self.bot.ping()),
            ("summary", lambda: self.bot.summary()),
            ("alerts", lambda: self.bot.alerts()),
            ("buildings", lambda: self.bot.buildings()),
            ("trees", lambda: self.bot.trees()),
            ("gatherables", lambda: self.bot.gatherables()),
            ("beavers", lambda: self.bot.beavers()),
            ("resources", lambda: self.bot.resources()),
            ("population", lambda: self.bot.population()),
            ("weather", lambda: self.bot.weather()),
            ("time", lambda: self.bot.time()),
            ("districts", lambda: self.bot.districts()),
            ("distribution", lambda: self.bot.distribution()),
            ("science", lambda: self.bot.science()),
            ("notifications", lambda: self.bot.notifications()),
            ("workhours", lambda: self.bot.workhours()),
            ("speed", lambda: self.bot.speed()),
            ("prefabs", lambda: self.bot.prefabs()),
            ("map", lambda: self.bot.map(120, 142, 122, 144)),
            ("tree_clusters", lambda: self.bot.tree_clusters()),
        ]
        for name, fn in reads:
            result = fn()
            self.check(f"GET {name}", not self.err(result),
                       json.dumps(result)[:100] if self.err(result) else "")

    def test_speed(self):
        print("\n=== speed ===\n")

        # save original
        orig = self.bot.speed()
        orig_speed = orig.get("speed", 1) if isinstance(orig, dict) else 1

        # set to 0 (pause)
        result = self.bot.set_speed(0)
        self.check("set speed 0", not self.err(result))

        # verify via debug
        verify = self.debug_get("_speedManager.CurrentSpeed")
        self.check("verify speed=0 via debug",
                   str(verify.get("value", "")) == "0",
                   f"got: {verify.get('value')}")

        # restore
        self.bot.set_speed(orig_speed)

    def test_placement_and_demolish(self):
        print("\n=== placement + demolish ===\n")

        # error cases
        tests = [
            ("occupied tile", lambda: self.bot.place_building("Path", 122, 133, 2), "occupied"),
            ("on water", lambda: self.bot.place_building("Path", 124, 130, 1), "water"),
            ("off map", lambda: self.bot.place_building("Path", 999, 999, 2), "no terrain"),
            ("unknown prefab", lambda: self.bot.place_building("Fake", 120, 130, 2), "not found"),
            ("invalid orientation", lambda: self.bot.place_building("Path", 120, 127, 2, orientation="bogus"), "invalid orientation"),
        ]
        for name, fn, expect_err in tests:
            result = fn()
            self.check(name, self.err(result) and expect_err in str(result["error"]),
                       json.dumps(result)[:100])

        # valid placement
        result = self.bot.place_building("Path", 119, 127, 2)
        self.check("valid placement", self.has(result, "id"))

        if self.has(result, "id"):
            placed_id = result["id"]

            # verify via map that tile is now occupied
            tile = self.bot.map(119, 127, 119, 127)
            tiles = tile.get("tiles", [])
            has_path = any(t.get("occupant") == "Path" for t in tiles)
            self.check("verify placement via map", has_path)

            # demolish
            dem = self.bot.demolish_building(placed_id)
            self.check("demolish", self.has(dem, "demolished") or not self.err(dem))

            # verify gone via map
            tile2 = self.bot.map(119, 127, 119, 127)
            tiles2 = tile2.get("tiles", [])
            no_path = not any(t.get("occupant") == "Path" for t in tiles2)
            self.check("verify demolish via map", no_path)

    def test_priority(self):
        print("\n=== priority ===\n")

        # find the DC (always exists)
        dc_id = None
        buildings = self.bot.buildings()
        if isinstance(buildings, list):
            for b in buildings:
                if "DistrictCenter" in str(b.get("name", "")):
                    dc_id = b.get("id")
                    break
        if not dc_id:
            self.skip("priority tests", "no DC found")
            return

        # set workplace priority
        result = self.bot.set_priority(dc_id, "VeryHigh", type="workplace")
        self.check("set priority VeryHigh",
                   self.has(result, "workplacePriority") and result["workplacePriority"] == "VeryHigh",
                   json.dumps(result)[:100])

        # restore
        self.bot.set_priority(dc_id, "Normal", type="workplace")

    def test_workers(self):
        print("\n=== workers ===\n")

        # find a workplace building
        dc_id = None
        buildings = self.bot.buildings()
        if isinstance(buildings, list):
            for b in buildings:
                if "DistrictCenter" in str(b.get("name", "")):
                    dc_id = b.get("id")
                    break
        if not dc_id:
            self.skip("worker tests", "no DC found")
            return

        # set workers to 1
        result = self.bot.set_workers(dc_id, 1)
        self.check("set workers 1",
                   self.has(result, "desiredWorkers") and result["desiredWorkers"] == 1,
                   json.dumps(result)[:100])

        # restore to 3
        self.bot.set_workers(dc_id, 3)

    def test_pause(self):
        print("\n=== pause/unpause ===\n")

        dc_id = self.find_building("DistrictCenter")
        if not dc_id:
            self.skip("pause tests", "no DC found")
            return

        # pause
        result = self.bot.pause_building(dc_id)
        self.check("pause building", not self.err(result))

        # verify via buildings endpoint
        buildings = self.bot.buildings()
        paused = False
        if isinstance(buildings, list):
            for b in buildings:
                if b.get("id") == dc_id:
                    paused = b.get("paused", False)
                    break
        self.check("verify paused", paused)

        # unpause
        self.bot.unpause_building(dc_id)

        # verify unpaused
        buildings2 = self.bot.buildings()
        unpaused = True
        if isinstance(buildings2, list):
            for b in buildings2:
                if b.get("id") == dc_id:
                    unpaused = not b.get("paused", True)
                    break
        self.check("verify unpaused", unpaused)

    def test_crops(self):
        print("\n=== crops ===\n")

        # plant on open ground
        result = self.bot.plant_crop(110, 130, 112, 132, 2, "Kohlrabi")
        self.check("plant crops", self.has(result, "planted") and result["planted"] == 9,
                   json.dumps(result)[:100])

        # plant on occupied (should skip)
        result2 = self.bot.plant_crop(119, 130, 122, 134, 2, "Kohlrabi")
        self.check("skip occupied tiles", self.has(result2, "skipped") and result2["skipped"] > 0)

        # plant on water (all skipped)
        result3 = self.bot.plant_crop(124, 128, 128, 132, 2, "Kohlrabi")
        self.check("skip water tiles",
                   self.has(result3, "planted") and result3["planted"] == 0 and result3.get("skipped", 0) > 0)

        # clear
        self.bot.clear_planting(110, 130, 112, 132, 2)
        self.bot.clear_planting(119, 130, 122, 134, 2)

    def test_tree_marking(self):
        print("\n=== tree marking ===\n")

        # mark trees in an area
        result = self.bot.mark_trees(125, 150, 135, 160, 4)
        self.check("mark trees", not self.err(result),
                   json.dumps(result)[:100] if self.err(result) else "")

        # verify via trees endpoint - some should be marked
        trees = self.bot.trees()
        marked = 0
        if isinstance(trees, list):
            marked = sum(1 for t in trees if t.get("marked"))
        self.check("verify trees marked", marked > 0, f"marked count: {marked}")

        # clear
        self.bot.clear_trees(125, 150, 135, 160, 4)

    def test_stockpile(self):
        print("\n=== stockpile ===\n")

        # find a tank
        tank_id = self.find_building("SmallTank") or self.find_building("Tank")
        if not tank_id:
            self.skip("stockpile tests", "no tank found")
            return

        # set good
        result = self.bot.set_good(tank_id, "Water")
        self.check("set stockpile good", not self.err(result),
                   json.dumps(result)[:100] if self.err(result) else "")

    def test_orientation(self):
        print("\n=== orientation ===\n")

        # find flat test area
        test_spot = None
        need = 5
        for cy in range(125, 145):
            for cx in range(70, 130):
                region = self.bot.map(cx, cy, cx + need - 1, cy + need - 1)
                tiles = region.get("tiles", [])
                if len(tiles) < need * need:
                    continue
                heights = set(t.get("terrain", 0) for t in tiles)
                if len(heights) != 1:
                    continue
                tz = heights.pop()
                if tz < 2:
                    continue
                occupants = [t for t in tiles if t.get("occupant") or t.get("water", 0) > 0]
                if occupants:
                    continue
                test = self.bot.place_building("Path", cx, cy, tz, orientation="south")
                if "id" in test:
                    self.bot.demolish_building(test["id"])
                    test_spot = (cx, cy, tz)
                    break
            if test_spot:
                break

        if not test_spot:
            self.skip("orientation tests", "no flat 5x5 area")
            return

        bx, by, bz = test_spot
        print(f"  using ({bx},{by},z={bz})\n")

        for prefab, sx, sy in [("FarmHouse.IronTeeth", 2, 2),
                                ("Barrack.IronTeeth", 3, 2),
                                ("IndustrialLumberMill.IronTeeth", 2, 3)]:
            for orient in ["south", "west", "north", "east"]:
                result = self.bot.place_building(prefab, bx, by, bz, orientation=orient)
                if "id" not in result:
                    if "not unlocked" in str(result.get("error", "")):
                        self.skip(f"{prefab.split('.')[0]} {orient}", "not unlocked")
                        continue
                    self.check(f"{prefab.split('.')[0]} {orient}", False, json.dumps(result)[:100])
                    continue

                # verify origin via map
                region = self.bot.map(bx - 1, by - 1, bx + sx, by + sy)
                occupied = [(t["x"], t["y"]) for t in region.get("tiles", [])
                            if t.get("occupant") and prefab.split(".")[0] in t["occupant"]]
                min_x = min(t[0] for t in occupied) if occupied else -1
                min_y = min(t[1] for t in occupied) if occupied else -1
                self.check(f"{prefab.split('.')[0]} {orient} origin=({min_x},{min_y})",
                           min_x == bx and min_y == by,
                           f"expected ({bx},{by})")
                self.bot.demolish_building(result["id"])

    def test_find_placement(self):
        print("\n=== find_placement ===\n")

        result = self.bot.find_placement("Inventor.IronTeeth", 120, 135, 155, 155)
        self.check("find_placement returns results",
                   self.has(result, "placements") and len(result.get("placements", [])) > 0)

        placements = result.get("placements", [])
        if placements:
            # first reachable result should be placeable
            reachable = [p for p in placements if p.get("reachable")]
            self.check("has reachable placements", len(reachable) > 0,
                       f"got {len(reachable)} reachable of {len(placements)}")

            if reachable:
                p = reachable[0]
                self.check("reachable has pathAccess", p.get("pathAccess") == True)

                # actually place it and verify no disconnect alert
                placed = self.bot.place_building("Inventor.IronTeeth",
                    p["x"], p["y"], p["z"], orientation=p["orientation"])
                if self.has(placed, "id"):
                    alerts = self.bot.alerts()
                    disconnected = False
                    if isinstance(alerts, list):
                        disconnected = any(
                            a.get("id") == placed["id"] and "not connected" in str(a.get("status", ""))
                            for a in alerts)
                    self.check("reachable spot is connected", not disconnected)
                    self.bot.demolish_building(placed["id"])
                else:
                    self.check("place at reachable spot", False, json.dumps(placed)[:100])

    def test_summary_projection(self):
        print("\n=== summary projection ===\n")

        result = self.bot.summary()
        if isinstance(result, dict):
            self.check("foodDays present", "foodDays" in result)
            self.check("waterDays present", "waterDays" in result)
            if "foodDays" in result:
                fd = result["foodDays"]
                self.check("foodDays > 0", isinstance(fd, (int, float)) and fd > 0, f"got: {fd}")
            if "waterDays" in result:
                wd = result["waterDays"]
                self.check("waterDays > 0", isinstance(wd, (int, float)) and wd > 0, f"got: {wd}")

    def test_map_moisture(self):
        print("\n=== map moisture ===\n")

        # check tiles near water for moist field
        result = self.bot.map(120, 135, 125, 140)
        tiles = result.get("tiles", [])
        moist_count = sum(1 for t in tiles if t.get("moist"))
        self.check("moist tiles near water", moist_count > 0, f"got {moist_count} moist tiles")

    def test_unlock(self):
        print("\n=== unlock ===\n")

        # check science points
        sci = self.bot.science()
        points = sci.get("points", 0) if isinstance(sci, dict) else 0
        if points < 50:
            self.skip("unlock test", f"only {points} science")
            return

        # find a cheap unlockable
        unlockables = sci.get("unlockables", [])
        target = None
        for u in unlockables:
            if not u.get("unlocked") and u.get("cost", 999) <= points:
                target = u
                break
        if not target:
            self.skip("unlock test", "nothing affordable")
            return

        before = points
        result = self.bot.unlock_building(target["name"])
        self.check(f"unlock {target['name']}",
                   self.has(result, "unlocked") and result["unlocked"] == True,
                   json.dumps(result)[:100])

        if self.has(result, "remaining"):
            expected = before - target["cost"]
            self.check("science deducted",
                       result["remaining"] == expected,
                       f"expected {expected}, got {result['remaining']}")


def main():
    runner = TestRunner()
    runner.run()


if __name__ == "__main__":
    main()
