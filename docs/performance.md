# Performance

Single source of truth for Timberbot API performance. All optimization decisions reference here.

## Entity tracking

Event-driven indexes via Timberborn's `EventBus`. Zero per-frame cost.

| Index | Mechanism | Per-frame cost | Rebuild trigger |
|---|---|---|---|
| `_buildingIndex` | `EntityInitializedEvent` / `EntityDeletedEvent` | **zero** | entity add/remove (instant) |
| `_naturalResourceIndex` | same | **zero** | entity add/remove (instant) |
| `_beaverIndex` | same | **zero** | entity add/remove (instant) |
| `_entityCache` (ID lookup) | same | **zero** | entity add/remove (instant) |
| `UpdateSingleton` | just `DrainRequests()` | **~0ms** when idle | N/A |
| `DrainRequests` | process up to 10 queued HTTP requests | **0ms** idle, ~10ms per request | incoming HTTP request |

## Endpoint performance (measured, 522 buildings / 2986 trees / 65 beavers / 4161 total)

### Optimized (use typed indexes)

| Endpoint | Iterates | Items | Measured | GetComponent/item | Notes |
|---|---|---|---|---|---|
| `ping` | none | 1 | **1ms** | 0 | Answered on listener thread |
| `summary` | all 3 indexes | 3000+500+65 | **7ms** | 3-4 per type | Three passes over subsets |
| `buildings` | `_buildingIndex` | 522 | **8ms** | 10-15 | `detail:basic` skips full serialization |
| `buildings detail:full` | `_buildingIndex` | 522 | **13ms** | 15+ | All fields including inventory, recipes, effectRadius |
| `trees` | `_naturalResourceIndex` | 2986 | **28ms** | 4 + IsInCuttingArea | **Highest cost** -- item count x GetComponent |
| `gatherables` | `_naturalResourceIndex` | ~150 | **<5ms** | 2 | Low item count |
| `beavers` | `_beaverIndex` | 65 | **5ms** | 5-8 | Needs iteration per beaver |
| `alerts` | `_buildingIndex` | 522 | **7ms** | 3 | Workplace + MechanicalNode + Reachability |
| `resources` | district centers | 13 | **7ms** | 0 | Iterates district registries, not entities |
| `weather` | none | 1 | **7ms** | 0 | Reads service fields |
| `prefabs` | building templates | 157 | **7ms** | 0 | Iterates BuildingService templates |

### Not optimized (still scan all entities)

| Endpoint | Line | What it does | Frequency | Could use |
|---|---|---|---|---|
| `BuildAllIndexes` | 222 | Initial index build | **once on load** | N/A (acceptable) |
| `CollectTreeClusters` | 517 | Grid-bucket grown trees | rare | `_naturalResourceIndex` |
| `CollectScan` | 562 | Radius-filtered survey | rare | all 3 indexes (needs all types) |
| `CollectMap` | 1326 | Region tile occupants | rare | all 3 indexes (needs all types) |
| `CollectWellbeing` | 1969 | Per-need-group breakdown | rare | `_beaverIndex` |
| `DemolishPathAt` | 2275 | Find path at tile | max 6x per route | `_buildingIndex` or coordinate index |
| `FindPlacement` | 2705 | Path/power tile scoring | rare | `_buildingIndex` |

## Thread model

| Location | Thread | Blocks game? |
|---|---|---|
| HTTP listener (accept + queue) | background | no |
| `ping`, `speed` responses | background (listener thread) | no |
| All other GET/POST | main thread via `DrainRequests` | **yes, for duration** |
| JSON serialization (`Respond`) | main thread | **yes** |

Python client calls are synchronous (send, wait, send next), so only 1 request is queued per frame. Burst of 7 calls = 7 frames, ~10ms each. No single frame exceeds 16ms budget except `trees` (28ms).

## Remaining bottlenecks (ordered by impact)

| # | Bottleneck | Cost | Root cause | Fix |
|---|---|---|---|---|
| 1 | **trees 28ms** | 2986 x 4 GetComponent | per-item component resolution every call | cache component refs in index struct |
| 2 | **buildings full 13ms** | 522 x 15 GetComponent | many optional components per building | cache frequently-accessed component refs |
| 3 | **tree_clusters full scan** | O(4161) | uses `_entityRegistry.Entities` | switch to `_naturalResourceIndex` |
| 4 | **wellbeing full scan** | O(4161) | uses `_entityRegistry.Entities` | switch to `_beaverIndex` |
| 5 | **find_placement full scan** | O(4161) | collects path/power tiles from all entities | switch to `_buildingIndex` |
| 6 | **demolish_path_at full scan** | O(4161) x up to 6 | finds path at coordinate | use `_buildingIndex` or coordinate lookup |
| 7 | **JSON on main thread** | ~1-3ms/response | `JsonConvert.SerializeObject` blocks | move serialization to listener thread |

## Late-game projections

| Metric | Current | Late-game (est) | Scaling |
|---|---|---|---|
| Buildings | 522 | 1500+ | linear with GetComponent count |
| Trees | 2986 | 5000+ | linear -- trees endpoint could hit 50ms+ |
| Beavers | 65 | 200+ | linear but low base count |
| Total entities | 4161 | 10000+ | only affects non-optimized endpoints |
| Burst (7 calls) | 62ms | ~120ms est | acceptable at 1 call/minute cadence |

## Test coverage

Performance tests in `timberbot/script/test_validation.py`:

- **Latency**: 10 endpoints x 5 iterations, all must be < 500ms
- **Cache consistency**: same endpoint called twice returns same count (no stale refs)
- **Cache invalidation**: place path -> count+1, demolish -> count back (EventBus works)
- **Burst**: 7 sequential calls < 3s total
