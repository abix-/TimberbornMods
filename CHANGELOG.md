# Changelog

## Unreleased

### Features
- Server-side pagination on list endpoints (default 100, `limit=0` for all, `offset` for paging)
- Server-side name and proximity filtering (`?name=Farm`, `?x=120&y=140&radius=20`)
- Structured error codes: all errors return machine-readable snake_case codes (`not_found`, `invalid_type`, `invalid_param`, etc.) with optional `detail` field
- RoutePath validates stairs/platform unlock before placing z-level changes

### Internal
- Automatic faction detection via `FactionService.Current.Id` (replaces hardcoded names)
- District population and resources cached in RefreshCachedState (zero live calls in summary)
- TimberbotJw `Result()`/`Error()` one-call response builders, `BeginArr`/`BeginObj`/`End` shortcuts
- Migrated 200+ Key().Value() calls to Prop/Obj/Arr/RawProp
- Migrated all 45 multi-line builders to BeginArr/BeginObj/End
- Cache cross-validation: `validate_all` covers buildings, beavers, natural resources, districts (3876 fields, 0 mismatches)
- 100% endpoint schema coverage: 57 checks across both TOON and JSON formats
- Comprehensive inline source comments

## v0.6.0 (2026-03-24)

Major performance overhaul. All GET requests now served on a background thread with zero main-thread cost.

### Architecture
- Event-driven entity indexes via Timberborn's EventBus -- zero per-frame entity scanning
- Double-buffered cached classes (`CachedBuilding`, `CachedBeaver`, `CachedNaturalResource`) for thread-safe background reads
- All GET requests served on background HTTP listener thread -- zero main-thread cost for reads
- Extracted 8 independent classes from god object: TimberbotService (7 DI), TimberbotRead (10 DI), TimberbotWrite (20 DI), TimberbotPlacement (13 DI), TimberbotEntityCache (5 DI), TimberbotWebhook (5 DI), TimberbotDebug (1 DI), TimberbotHttpServer
- TimberbotJw: fluent zero-alloc JSON writer replacing Dictionary + Newtonsoft serialization
- Cadenced cache refresh (default 1s, configurable via settings.json)
- TimberbotLog: file-based error logging with timestamps and stack traces
- Zero-alloc hot path confirmed via 10K-iteration benchmark (0 GC0 across 760K API calls)

### Features
- 68 webhook push notification events (drought, death, construction, weather, power, wonders) with 200ms batching and circuit breaker
- Separate `/api/trees` and `/api/crops` endpoints (replaces combined natural_resources)
- `detail` param on buildings and beavers (`basic`/`full`/`id:<id>`)
- `effectRadius` on ranged effect buildings (monuments)
- `productionProgress` and `readyToProduce` on manufactories
- Per-good inventory breakdown on buildings
- `liftingCapacity` on beavers
- `/api/benchmark` endpoint for profiling all read endpoints
- Live `top` dashboard with colony overview

### Fixes
- Pause/unpause uses game Pause()/Resume() methods for proper UI icon update

### Tests
- 247 integration tests, 2000/2000 reliability across 20 endpoints
- Save-agnostic tests using find_placement for dynamic coords

## v0.5.5 (2026-03-24)

### Features
- Building material costs and unlock status on prefabs endpoint
- Per-building stock and capacity for tanks, warehouses, stockpiles
- Available recipes and current recipe on manufactories
- Breeding pod nutrient status
- Beaver activity from game status system
- Clutch engage/disengage endpoint
- Per-beaver need breakdown (every unmet need by name with points and wellbeing)
- `find_planting`: valid irrigated spots within farmhouse range or area
- `building_range`: work radius for farmhouse, lumberjack, forester, gatherer, scavenger, DC

### Tests
- 118 integration tests

## v0.5.3 (2026-03-24)

- Compass names for orientation everywhere (south, west, north, east)
- Remove number and single-letter orientation fallbacks
- PATH setup for timberbot.py CLI

## v0.5.2 (2026-03-24)

### Features
- Wellbeing breakdown endpoint (per-category: Social, Fun, Nutrition, Aesthetics, Awe)

### Fixes
- Building placement for water buildings (SwimmingPool, DoubleShower)
- Crop planting validation matches player UI behavior
- Placement on dead standing trees no longer incorrectly allowed

### Tests
- 91 integration tests

## v0.5.1 (2026-03-23)

### Fixes
- Fix unlock_building deducting science twice

## v0.5.0 (2026-03-23)

### Features
- `find_placement`: scans area for valid building spots with reachability, path access, power adjacency
- `place_path`: auto-builds stairs and platforms for multi-level z-changes
- `summary` includes `foodDays` and `waterDays` resource projections
- `map` returns `moist` field for irrigated tiles
- Generic `debug` endpoint for inspecting game internals via reflection

### Fixes
- Fix crash when reloading a save (HTTP server port conflict)
- `unlock_building` deducts science and updates UI matching the player's unlock flow
- `PlaceBuilding` validates stackable blocks (platforms) as valid build surfaces

### Tests
- 81 integration tests

## v0.4.8 (2026-03-23)

- Terrain height shading on map tiles (darker = lower, lighter = higher)
- Empty ground displays z-level digit instead of dots
- Height legend when multiple z-levels are in view

## v0.4.7 (2026-03-23)

### Features
- `--json` flag for full JSON output alongside default TOON format
- Summary includes housing, employment, wellbeing, science, and alerts in one call
- New endpoints: hauler priority, manufactory recipes, farmhouse planting priority, forester tree priority
- Alerts, tree clusters, and scan now run server-side
- Clean names in all output (no more Clone/IronTeeth suffixes)

### Fixes
- Building unlock works reliably in game UI for all buildings
- Critical needs count only truly low needs (was counting all survival needs)

### Tests
- 88 integration tests

## v0.4.6 (2026-03-22)

- Badwater detection: `badwater` field on water tiles (0-1 contamination scale)
- Soil contamination: `contaminated` field on land tiles near badwater
- Reject placement when building would clip underground (z must match terrain height)
- Dead trees (stumps) no longer block placement

## v0.4.5 (2026-03-22)

- Science endpoint returns all 126 unlockable buildings with name, cost, unlock status
- `unlock_building` updates the game UI toolbar immediately
- Placing locked buildings blocked with error showing science cost needed
- 72 integration tests

## v0.4.4 (2026-03-22)

- Separate `constructionPriority` and `workplacePriority` on buildings
- `set_priority` accepts `type:workplace` or `type:construction`

## v0.4.3 (2026-03-22)

- Soil contamination on map tiles
- Per-building nominal power input/output
- District migration between districts
- Dwelling occupants (dwellers/maxDwellers)
- Clutch status on buildings
- Beaver home field
- Wellbeing tiers in TOON output
- 67 integration tests

## v0.4.2 (2026-03-22)

- Pagination on list endpoints (limit/offset)
- Beavers: isBot, contaminated fields
- Buildings: isWonder/wonderActive
- Work schedule read/write
- 60 integration tests

## v0.4.1 (2026-03-22)

- Construction progress on buildings (buildProgress, materialProgress, hasMaterials)
- Building inventory contents
- Beaver workplace assignment
- Notifications endpoint
- Alerts helper (unstaffed, unpowered, unreachable)

## v0.4.0 (2026-03-22)

### Features
- Science points endpoint, unlock buildings via API
- Distribution read/write (import/export per good per district)
- Buildings: reachable, powered, power network fields
- Tree cluster finder for optimal lumberjack placement
- AI playbook (docs/timberbot.md) works as Claude Code skill

### Fixes
- Beaver needs filter shows only active needs
- Speed scale matches game UI (0=pause, 1=normal, 2=fast, 3=fastest)

## v0.3.8 (2026-03-22)

- TOON format CLI output (compact, token-efficient for AI)
- `beavers` command with wellbeing and critical needs
- Requires `pip install toons` (falls back to JSON)

## v0.3.7 (2026-03-22)

- Named orientations (south/west/north/east instead of numbers)
- Orientation origin correction for multi-tile buildings
- 39 integration tests

## v0.3.6 (2026-03-22)

- Crop planting validation (skips buildings, water, invalid terrain)
- Initial regression test suite (test_validation.py)

## v0.3.5 (2026-03-22)

- C#-side placement validation (occupancy, water, terrain, off-map checks)
- Orientation-aware footprint computation
- Demolition debris no longer blocks placement

## v0.3.4 (2026-03-22)

- TOON format map output
- Colored roguelike map visualization
- Unique crop type letters

## v0.3.3 (2026-03-22)

- Summary includes tree stats (marked grown, marked seedlings, unmarked grown)

## v0.3.2 (2026-03-22)

- Full building footprints on map
- Seedling vs grown tree distinction
- Building entrance coordinates
- Planting fix: crops now appear in-game
- Water building placement validation

## v0.3.1 (2026-03-22)

- Initial release with timberbot.py client included
