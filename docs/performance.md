# Performance

Single source of truth for Timberbot API performance. All optimization decisions reference here.

## Entity tracking

Event-driven indexes via Timberborn's `EventBus` with cached component refs. Zero per-frame cost, zero `GetComponent` calls per request.

| Index | Type | Mechanism | Per-frame cost | Rebuild trigger |
|---|---|---|---|---|
| `_buildingIndex` | `List<CachedBuilding>` | `EntityInitializedEvent` / `EntityDeletedEvent` | **zero** | entity add/remove (instant) |
| `_naturalResourceIndex` | `List<CachedNaturalResource>` | same | **zero** | entity add/remove (instant) |
| `_beaverIndex` | `List<EntityComponent>` | same | **zero** | entity add/remove (instant) |
| `_entityCache` | `Dictionary<int, EntityComponent>` | same | **zero** | entity add/remove (instant) |
| `UpdateSingleton` | -- | just `DrainRequests()` | **~0ms** when idle | N/A |

### Cached component refs

`CachedBuilding` and `CachedNaturalResource` structs resolve all component references once at entity-add time. Endpoints read live property values (`.Paused`, `.IsGrown`, `.Wellbeing`) from cached refs without calling `GetComponent<T>()`.

| Struct | Fields cached | GetComponent calls saved per item |
|---|---|---|
| `CachedBuilding` | BlockObject, Pausable, Floodgate, BuilderPrio, Workplace, WorkplacePrio, Reachability, Mechanical, Status, PowerNode, Site, Inventories, Wonder, Dwelling, Clutch, Manufactory, BreedingPod, RangedEffect | **18** |
| `CachedNaturalResource` | BlockObject, Living, Cuttable, Gatherable, Growable | **5** |
| `_beaverIndex` | (not cached -- only 65 items, not worth it) | 0 |

## Endpoint performance (measured, 522 buildings / 2986 trees / 65 beavers / 4161 total)

### Optimized (cached struct indexes)

| Endpoint | Iterates | Items | Measured | GetComponent/item | Notes |
|---|---|---|---|---|---|
| `ping` | none | 1 | **1ms** | 0 | Answered on listener thread |
| `summary` | all 3 indexes | 3000+500+65 | **7ms** | 0 (buildings/trees), 2-3 (beavers) | Three passes over subsets |
| `buildings` | `_buildingIndex` | 522 | **8ms** | **0** | `detail:basic` skips full serialization |
| `buildings detail:full` | `_buildingIndex` | 522 | **13ms** | **0** | All fields including inventory, recipes, effectRadius |
| `trees` | `_naturalResourceIndex` | 2986 | **25ms** | **0** | Remaining cost: dict alloc + IsInCuttingArea per item |
| `gatherables` | `_naturalResourceIndex` | ~150 | **<5ms** | **0** | Low item count |
| `beavers` | `_beaverIndex` | 65 | **8ms** | 5-8 | NeedManager iteration per beaver |
| `alerts` | `_buildingIndex` | 522 | **7ms** | **0** | Workplace + PowerNode + Reachability from cache |
| `resources` | district centers | 13 | **7ms** | 0 | Iterates district registries, not entities |
| `weather` | none | 1 | **7ms** | 0 | Reads service fields |
| `prefabs` | building templates | 157 | **7ms** | 0 | Iterates BuildingService templates |

### Not optimized (still scan all entities)

| Endpoint | What it does | Frequency | Could use |
|---|---|---|---|
| `BuildAllIndexes` | Initial index build | **once on load** | N/A (acceptable) |
| `CollectTreeClusters` | Grid-bucket grown trees | rare | `_naturalResourceIndex` |
| `CollectScan` | Radius-filtered survey | rare | all 3 indexes |
| `CollectMap` | Region tile occupants | rare | all 3 indexes |
| `CollectWellbeing` | Per-need-group breakdown | rare | `_beaverIndex` |
| `DemolishPathAt` | Find path at tile | max 6x per route | `_buildingIndex` |
| `FindPlacement` | Path/power tile scoring | rare | `_buildingIndex` |

## Thread model

| Location | Thread | Blocks game? |
|---|---|---|
| HTTP listener (accept + queue) | background | no |
| `ping`, `speed` responses | background (listener thread) | no |
| All other GET/POST | main thread via `DrainRequests` | **yes, for duration** |
| JSON serialization (`Respond`) | main thread | **yes** |

Python client calls are synchronous (send, wait, send next), so only 1 request is queued per frame. Burst of 7 calls = 7 frames, ~10ms each.

## Remaining bottlenecks (ordered by impact)

| # | Bottleneck | Cost | Root cause | Fix |
|---|---|---|---|---|
| 1 | **trees 25ms** | 2986 items | per-item Dictionary alloc + `IsInCuttingArea` + property reads | pre-allocated result arrays or TOON serialization bypass |
| 2 | **buildings full 13ms** | 522 items | per-item Dictionary alloc + inventory iteration | same |
| 3 | **tree_clusters full scan** | O(4161) | uses `_entityRegistry.Entities` | switch to `_naturalResourceIndex` |
| 4 | **wellbeing full scan** | O(4161) | uses `_entityRegistry.Entities` | switch to `_beaverIndex` |
| 5 | **find_placement full scan** | O(4161) | collects path/power tiles from all entities | switch to `_buildingIndex` |
| 6 | **demolish_path_at full scan** | O(4161) x up to 6 | finds path at coordinate | use `_buildingIndex` |
| 7 | **JSON on main thread** | ~1-3ms/response | `JsonConvert.SerializeObject` blocks | move serialization to listener thread |

## Resolved bottlenecks

| Bottleneck | Was | Fix applied |
|---|---|---|
| GetComponent per item (trees) | 5 calls x 2986 items/request | cached component refs in `CachedNaturalResource` struct |
| GetComponent per item (buildings) | 18 calls x 522 items/request | cached component refs in `CachedBuilding` struct |
| Full entity scan per endpoint | O(4161) every call | event-driven typed indexes via `EventBus` |
| Per-frame index rebuild | O(4161) every frame | eliminated -- indexes update on entity add/remove only |
| Per-frame entity cache rebuild | O(4161) every frame | eliminated -- `_entityCache` updates via EventBus |
| Pause/unpause missing UI icon | `.Paused` set directly | use `Pause()`/`Resume()` methods |

## Optimization history

| Change | trees | buildings | buildings full | burst (7 calls) |
|---|---|---|---|---|
| Baseline (full entity scan) | ~50ms est | ~20ms est | ~30ms est | ~150ms est |
| Typed entity indexes | 29ms | 9ms | 10ms | 67ms |
| Event-driven (EventBus) | 28ms | 9ms | 13ms | 62ms |
| Cached component refs | **25ms** | **8ms** | **13ms** | **64ms** |

GetComponent elimination reduced trees by ~15%. The remaining ~85% of cost is Dictionary allocation, property reads, and `IsInCuttingArea` per tree. Further gains require changing the serialization approach (not the entity access pattern).

## Late-game projections

| Metric | Current | Late-game (est) | Scaling |
|---|---|---|---|
| Buildings | 522 | 1500+ | linear with item count (dict alloc) |
| Trees | 2986 | 5000+ | linear -- trees endpoint could hit 40ms+ |
| Beavers | 65 | 200+ | linear but low base count |
| Total entities | 4161 | 10000+ | only affects non-optimized endpoints |
| Burst (7 calls) | 64ms | ~110ms est | acceptable at 1 call/minute cadence |

## Test coverage

Performance tests in `timberbot/script/test_validation.py`:

- **Latency**: 10 endpoints x 5 iterations, all must be < 500ms
- **Cache consistency**: same endpoint called twice returns same count (no stale refs)
- **Cache invalidation**: place path -> count+1, demolish -> count back (EventBus works)
- **Burst**: 7 sequential calls < 3s total
