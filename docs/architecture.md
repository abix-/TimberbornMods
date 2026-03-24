# Architecture

How Timberbot's HTTP API works under the hood.

## Thread model

```
MAIN THREAD (Unity)                    BACKGROUND THREAD (HttpListener)
========================               ================================
UpdateSingleton() [60fps]              ListenLoop() [blocking accept]
  |                                      |
  +-- RefreshCachedState() [1s cadence]  +-- GET request arrives
  |     |                                |     |
  |     +-- update _*Write buffers       |     +-- RouteRequest() reads _*Read
  |     +-- swap refs (atomic)           |     +-- StringBuilder serialization
  |     |   _*Read <-> _*Write           |     +-- Respond() sends JSON
  |     |                                |
  +-- DrainRequests() [POST only]        +-- POST request arrives
        |                                      |
        +-- RouteRequest() mutates game        +-- queue to _pending
        +-- Respond() sends JSON               |
                                               (main thread processes next frame)
```

## Double buffer

`DoubleBuffer<T>` generic class manages two pre-allocated lists per entity type. Main thread writes to `.Write`, background reads from `.Read`. Ref swap publishes updates.

```
Cadence N:   main refreshes _buildings.Write  |  background reads _buildings.Read
             [_buildings.Swap()]
Cadence N+1: main refreshes old .Read         |  background reads freshly updated buffer
```

**Rules:**
- `DoubleBuffer.Add(writeItem, readItem)`: add to both buffers with separate reference-type instances
- `DoubleBuffer.Add(item)`: safe for value-only structs (no reference fields)
- `DoubleBuffer.RemoveAll()`: removes from both buffers
- `RefreshCachedState`: updates `.Write` only, then `.Swap()`
- No copy-back. Old read buffer (now write) has same entities, 1-cadence-stale values
- Background thread never modifies any buffer. Zero contention.

**Reference-type fields** (`List<T>`, `Dictionary<K,V>`) must be separate instances per buffer. Shared references cause mutation-during-read corruption. Use `Add(writeItem, readItem)` with distinct instances. Immutable-after-add fields (e.g. `OccupiedTiles`) are safe to share.

## Entity lifecycle

```
Entity created (building placed, beaver born, tree spawned)
  -> EntityInitializedEvent (EventBus)
  -> OnEntityInitialized()
  -> AddToIndexes(): resolve component refs, add to both read+write buffers

Entity destroyed (building demolished, beaver died, tree cut)
  -> EntityDeletedEvent (EventBus)
  -> OnEntityDeleted()
  -> RemoveFromIndexes(): remove from both buffers + entity cache
```

## Cached structs

Component references resolved once at entity-add time. Mutable state refreshed on main thread at cadence. Serialized via `Jw` (JsonWriter) helper for zero-alloc JSON.

```
CachedBuilding {
  // immutable refs (set at add-time, never refreshed)
  Entity, Id, Name, BlockObject, Pausable, Floodgate, Workplace, ...
  HasFloodgate, HasClutch, HasWonder, IsGenerator, IsConsumer, ...
  OccupiedTiles (immutable List, safe to share between buffers)

  // mutable primitives (refreshed by RefreshCachedState)
  Finished, Paused, Unreachable, Powered, X, Y, Z, Orientation,
  AssignedWorkers, DesiredWorkers, FloodgateHeight, BuildProgress, ...

  // mutable reference types (SEPARATE instances per buffer!)
  Recipes (List<string>), Inventory (Dict), NutrientStock (Dict)
}

CachedNaturalResource {
  // immutable refs
  Id, Name, BlockObject, Living, Cuttable, Gatherable, Growable
  // mutable primitives (all value types -- safe to share)
  X, Y, Z, Alive, Grown, Growth, Marked
}

CachedBeaver {
  // immutable refs
  Id, Name, IsBot, NeedMgr, WbTracker, Worker, Life, Carrier, ...
  // mutable primitives
  Wellbeing, X, Y, Z, Workplace, District, HasHome, ...
  // mutable reference type (SEPARATE instance per buffer!)
  Needs (List<CachedNeed>)
}
```

## Serialization

High-volume endpoints (buildings, trees, beavers) use `Jw` (JsonWriter) helper with `StringBuilder` for zero-alloc JSON. Pre-allocated `_sbBuildings`, `_sbTrees`, `_sbBeavers` fields, `.Clear()` per request.

Other endpoints use `Dictionary<string, object>` + `JsonConvert.SerializeObject` (acceptable for low-volume data).

Pre-serialized strings detected in `Respond()`: `data is string s ? s : JsonConvert.SerializeObject(data)`.

## Settings

`settings.json` in mod folder (`Documents/Timberborn/Mods/Timberbot/`):

```json
{
  "refreshIntervalSeconds": 1.0,
  "debugEndpointEnabled": false,
  "httpPort": 8085
}
```

- `refreshIntervalSeconds`: how often mutable state is snapshotted (default 1s)
- `debugEndpointEnabled`: enable `/api/debug` reflection endpoint (default off)
- `httpPort`: HTTP server port (default 8085)

Loaded once on game load. Missing file or fields use defaults.

## Request flow

### GET (background thread, zero main-thread cost)

```
HTTP request -> ListenLoop -> RouteRequest -> read _*Read buffers
  -> StringBuilder or Dict serialization -> Respond -> HTTP response
```

### POST (main thread via queue)

```
HTTP request -> ListenLoop -> parse body -> enqueue PendingRequest
  -> [next frame] DrainRequests -> RouteRequest -> mutate game state
  -> Respond -> HTTP response
```

## Data staleness

Mutable values (paused, workers, wellbeing) are up to `refreshIntervalSeconds` stale. Entity presence (which buildings/trees exist) is always current via EventBus. For a bot polling once per minute, 1s staleness is imperceptible.
