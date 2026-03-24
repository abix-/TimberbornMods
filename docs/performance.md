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
| `ping` | none | 1 | **1ms** | 0 | Listener thread |
| `summary` | all 3 indexes | 3000+500+65 | **2ms** | 0 (buildings/trees), 2-3 (beavers) | Listener thread, three passes over subsets |
| `buildings` | `_buildingIndex` | 522 | **6.5ms** | **0** | Listener thread, `detail:basic` skips full serialization |
| `buildings detail:full` | `_buildingIndex` | 522 | **8ms** | **0** | Listener thread, all fields |
| `trees` | `_naturalResourceIndex` | 2986 | **23ms** | **0** | Listener thread. Remaining cost: dict alloc + IsInCuttingArea |
| `gatherables` | `_naturalResourceIndex` | ~150 | **<3ms** | **0** | Listener thread |
| `beavers` | `_beaverIndex` | 65 | **3ms** | 5-8 | Listener thread |
| `alerts` | `_buildingIndex` | 522 | **1.4ms** | **0** | Listener thread |
| `resources` | district centers | 13 | **1.2ms** | 0 | Listener thread |
| `weather` | none | 1 | **0.8ms** | 0 | Listener thread |
| `prefabs` | building templates | 157 | **4ms** | 0 | Listener thread |

### Still scan all entities (by design)

| Endpoint | What it does | Frequency | Why not indexed |
|---|---|---|---|
| `BuildAllIndexes` | Initial index build | **once on load** | populates all indexes |
| `CollectScan` | Radius-filtered survey | rare | needs all entity types in region |
| `CollectMap` | Region tile occupants | rare | needs all entity types in region |

## Thread model

| Location | Thread | Blocks game? |
|---|---|---|
| HTTP listener (accept + queue) | background | no |
| All GET requests (reads) | background (listener thread) | **no** |
| All POST requests (writes) | main thread via `DrainRequests` | yes, for duration |
| JSON serialization (`Respond`) | same thread as request | no for GETs |

All reads served on the listener thread. Zero main-thread cost for GET-only bot turns. Writes (POST) still queue to main thread for safe game state mutation. Per-item try/catch handles the race window where an entity is destroyed mid-read.

## Remaining bottlenecks (ordered by impact)

| # | Bottleneck | Cost | Root cause | Fix |
|---|---|---|---|---|
| 1 | **trees 23ms** | 2986 items | per-item Dictionary alloc + `IsInCuttingArea` + property reads | pre-allocated result arrays or TOON serialization bypass |
| 2 | **buildings full 8ms** | 522 items | per-item Dictionary alloc + inventory iteration | same |
| 3 | **Unity GC spikes** | random 0.5-2s | Unity garbage collector freezes all threads | unavoidable from mod code |

## Resolved bottlenecks

| Bottleneck | Was | Fix applied |
|---|---|---|
| GetComponent per item (trees) | 5 calls x 2986 items/request | cached component refs in `CachedNaturalResource` struct |
| GetComponent per item (buildings) | 18 calls x 522 items/request | cached component refs in `CachedBuilding` struct |
| Full entity scan per endpoint | O(4161) every call | event-driven typed indexes via `EventBus` |
| Per-frame index rebuild | O(4161) every frame | eliminated -- indexes update on entity add/remove only |
| Per-frame entity cache rebuild | O(4161) every frame | eliminated -- `_entityCache` updates via EventBus |
| tree_clusters full scan | O(4161) | switched to `_naturalResourceIndex` with cached refs |
| wellbeing full scan | O(4161) | switched to `_beaverIndex` |
| find_placement full scan | O(4161) | switched to `_buildingIndex` with cached refs |
| demolish_path_at full scan | O(4161) x up to 6 | switched to `_buildingIndex` with cached refs |
| All reads blocking main thread | ~7ms overhead per call | GETs served on listener thread, zero main-thread cost |
| JSON serialization on main thread | ~1-3ms/response | now serializes on listener thread for GETs |
| Pause/unpause missing UI icon | `.Paused` set directly | use `Pause()`/`Resume()` methods |

## Optimization history

| Change | trees | buildings | buildings full | burst (7 calls) |
|---|---|---|---|---|
| Baseline (full entity scan) | ~50ms est | ~20ms est | ~30ms est | ~150ms est |
| Typed entity indexes | 29ms | 9ms | 10ms | 67ms |
| Event-driven (EventBus) | 28ms | 9ms | 13ms | 62ms |
| Cached component refs | 25ms | 8ms | 13ms | 64ms |
| GETs on listener thread | **23ms** | **6.5ms** | **8ms** | **39ms** |

The ~7ms floor in earlier measurements was main-thread frame scheduling overhead. Moving GETs to the listener thread eliminated it. Remaining cost is per-item Dictionary allocation, property reads, and `IsInCuttingArea`. Main-thread cost for reads is now **zero**.

## Late-game projections

| Metric | Current | Late-game (est) | Scaling |
|---|---|---|---|
| Buildings | 522 | 1500+ | linear with item count (dict alloc) |
| Trees | 2986 | 5000+ | linear -- trees endpoint could hit 40ms+ |
| Beavers | 65 | 200+ | linear but low base count |
| Total entities | 4161 | 10000+ | only affects CollectScan/CollectMap (rare, region-bounded) |
| Burst (7 calls) | 39ms | ~70ms est | acceptable at 1 call/minute cadence |

## Test coverage

Performance tests in `timberbot/script/test_validation.py`:

- **Latency**: 10 endpoints x 5 iterations, all must be < 500ms
- **Cache consistency**: same endpoint called twice returns same count (no stale refs)
- **Cache invalidation**: place path -> count+1, demolish -> count back (EventBus works)
- **Burst**: 7 sequential calls < 3s total
