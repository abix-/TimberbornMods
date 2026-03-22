---
name: timberbot-cli
description: Python script for reading and controlling a running Timberborn game via the Timberbot mod. Use when interacting with Timberborn over HTTP.
user-invocable: false
version: "4.0"
updated: "2026-03-21"
---
# Timberbot

`timberbot.py` is a single Python script that talks to the Timberbot mod over HTTP (port 8085). Download it, run it. No install needed beyond `pip install requests`.

All args are `key:value` pairs. Output is JSON (except `watch` and `scan`). Run with no args to see all methods with usage.

## Examples

```bash
python timberbot.py                                          # list all methods with usage
python timberbot.py summary                                  # full colony snapshot
python timberbot.py buildings                                # list all buildings with IDs
python timberbot.py set_speed speed:3                        # fast forward
python timberbot.py place_building prefab:Path x:100 y:130 z:2
python timberbot.py place_path x1:100 y1:130 x2:110 y2:130 z:2
python timberbot.py demolish_building building_id:12345
python timberbot.py mark_trees x1:100 y1:100 x2:110 y2:110 z:2
python timberbot.py plant_crop x1:100 y1:100 x2:105 y2:105 z:2 crop:Carrot
python timberbot.py set_priority building_id:12345 priority:VeryHigh
python timberbot.py scan x:120 y:140 radius:10
python timberbot.py find source:buildings name:Lumber x:100 y:130 radius:20
python timberbot.py watch                                    # live terminal dashboard
```

## Helpers (Python-only, not HTTP endpoints)

| Command | Description |
|---------|-------------|
| `place_path x1:N y1:N x2:N y2:N z:N` | place straight line of paths (horizontal or vertical) |
| `scan x:N y:N radius:10` | ASCII grid of terrain, water, buildings, trees |
| `find source:buildings name:NAME x:N y:N radius:20` | find entities by name and/or proximity |
| `watch` | live terminal dashboard (polls every 3s) |

## IDs and values

- **Building IDs**: from `buildings` output. Ephemeral per game session
- **Prefab names**: from `prefabs` (e.g. `LumberjackFlag.IronTeeth`, `DeepWaterPump.IronTeeth`)
- **Good names**: `Log`, `Plank`, `Water`, `Berries`, etc.
- **Priority**: `VeryLow`, `Normal`, `VeryHigh`
- **Orientation**: 0-3 (rotates 90 degrees each step)
- **Crops**: `Kohlrabi`, `Cassava`, `Carrot`, `Potato`, `Wheat`, `Sunflower`, etc.

## Full API reference

See [docs/api-reference.md](../../docs/api-reference.md) for all HTTP endpoints, request/response formats, and the vanilla API.
