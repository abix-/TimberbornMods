# Changelog

All notable changes to Timberbot are documented here. Links point to the commit where each feature was added.

[Unreleased]: https://github.com/abix-/TimberbornMods/compare/v0.6.2...HEAD

## [Unreleased]

## [v0.6.2] (2026-03-25)

- [breaking] Building endpoints moved under /api/building/ (floodgate, priority, workers, recipe, hauling, farmhouse, plantable)
- [breaking] /api/path/route -> /api/path/place, /api/map POST -> /api/tiles, /api/scan removed
- [breaking] /api/natural_resources removed -- use /api/trees and /api/crops
- [breaking] List endpoints return paginated wrapper (use limit=0 for flat array)
- [breaking] Error format changed to "code: detail" (parse prefix before : for machine-readable code)
- [feature] 68 webhook push events with batching and circuit breaker ([`ff4fb12`][ff4fb12], [`a9d5fcb`][a9d5fcb], [`f47484e`][f47484e])
- [feature] Separate /api/trees and /api/crops endpoints ([`c25de95`][c25de95])
- [feature] /api/benchmark endpoint ([`c24c4b5`][c24c4b5])
- [feature] Live top dashboard ([`cfec1a5`][cfec1a5])
- [feature] Flood validation in find_placement ([`04feca0`][04feca0])
- [feature] Server-side pagination on list endpoints ([`dea094e`][dea094e])
- [feature] Server-side name and proximity filtering ([`7c7fb80`][7c7fb80])
- [feature] Structured error codes: "code: detail" format ([`05b5a0d`][05b5a0d], [`24ba215`][24ba215])
- [feature] RoutePath validates stairs/platform unlock before placing ([`668a44b`][668a44b])
- [feature] Python client TimberbotError with .code and .response ([`1670124`][1670124])
- [feature] All errors routed through TimberbotJw.Error() for consistent JSON output
- [fix] JsonWriter double-comma bug and UTF-8 BOM ([`38597be`][38597be], [`e65f7ed`][e65f7ed])
- [fix] Districts TOON format double-comma
- [fix] GetBuildingTemplate throwing on unknown prefabs ([`4804450`][4804450])
- [fix] DoubleBuffer collection modified during enumeration
- [fix] Exception messages breaking JSON output
- [internal] Extract 8 classes from god object ([`8e0c841`][8e0c841], [`63655ec`][63655ec], [`6caf19c`][6caf19c], [`558b156`][558b156], [`55e7501`][55e7501], [`67904d6`][67904d6])
- [internal] TimberbotJw fluent zero-alloc JSON writer ([`329d3ac`][329d3ac])
- [internal] TimberbotLog file-based error logging ([`a73cf1a`][a73cf1a])
- [internal] Zero-alloc hot path confirmed ([`8b191b2`][8b191b2])
- [internal] Cached classes struct to class, eliminate 144K field copies/sec ([`13da06a`][13da06a])
- [internal] District population/resources cached in RefreshCachedState ([`e50c432`][e50c432])
- [internal] Faction detection via FactionService.Current.Id ([`9d29f3e`][9d29f3e])
- [internal] TimberbotJw Result()/Error() one-call builders ([`f97f8d8`][f97f8d8])
- [internal] BeginArr/BeginObj/End shortcuts, migrate 45 builders ([`0938b0c`][0938b0c])
- [internal] Migrate 200+ calls to Prop/Obj/Arr/RawProp ([`1fa9cd1`][1fa9cd1])
- [internal] Cache cross-validation: 3876 fields, 0 mismatches ([`f7990b9`][f7990b9])
- [internal] 100% endpoint schema coverage: 57 checks ([`b81e951`][b81e951])

[v0.6.2]: https://github.com/abix-/TimberbornMods/releases/tag/v0.6.2
[dea094e]: https://github.com/abix-/TimberbornMods/commit/dea094e
[7c7fb80]: https://github.com/abix-/TimberbornMods/commit/7c7fb80
[05b5a0d]: https://github.com/abix-/TimberbornMods/commit/05b5a0d
[668a44b]: https://github.com/abix-/TimberbornMods/commit/668a44b
[9d29f3e]: https://github.com/abix-/TimberbornMods/commit/9d29f3e
[e50c432]: https://github.com/abix-/TimberbornMods/commit/e50c432
[f97f8d8]: https://github.com/abix-/TimberbornMods/commit/f97f8d8
[0938b0c]: https://github.com/abix-/TimberbornMods/commit/0938b0c
[1fa9cd1]: https://github.com/abix-/TimberbornMods/commit/1fa9cd1
[f7990b9]: https://github.com/abix-/TimberbornMods/commit/f7990b9
[b81e951]: https://github.com/abix-/TimberbornMods/commit/b81e951
[8e0c841]: https://github.com/abix-/TimberbornMods/commit/8e0c841
[63655ec]: https://github.com/abix-/TimberbornMods/commit/63655ec
[6caf19c]: https://github.com/abix-/TimberbornMods/commit/6caf19c
[558b156]: https://github.com/abix-/TimberbornMods/commit/558b156
[55e7501]: https://github.com/abix-/TimberbornMods/commit/55e7501
[67904d6]: https://github.com/abix-/TimberbornMods/commit/67904d6
[329d3ac]: https://github.com/abix-/TimberbornMods/commit/329d3ac
[a73cf1a]: https://github.com/abix-/TimberbornMods/commit/a73cf1a
[8b191b2]: https://github.com/abix-/TimberbornMods/commit/8b191b2
[13da06a]: https://github.com/abix-/TimberbornMods/commit/13da06a
[ff4fb12]: https://github.com/abix-/TimberbornMods/commit/ff4fb12
[a9d5fcb]: https://github.com/abix-/TimberbornMods/commit/a9d5fcb
[f47484e]: https://github.com/abix-/TimberbornMods/commit/f47484e
[c25de95]: https://github.com/abix-/TimberbornMods/commit/c25de95
[c24c4b5]: https://github.com/abix-/TimberbornMods/commit/c24c4b5
[cfec1a5]: https://github.com/abix-/TimberbornMods/commit/cfec1a5
[24ba215]: https://github.com/abix-/TimberbornMods/commit/24ba215
[1670124]: https://github.com/abix-/TimberbornMods/commit/1670124
[4804450]: https://github.com/abix-/TimberbornMods/commit/4804450
[ee773ec]: https://github.com/abix-/TimberbornMods/commit/ee773ec
[04feca0]: https://github.com/abix-/TimberbornMods/commit/04feca0
[38597be]: https://github.com/abix-/TimberbornMods/commit/38597be
[e65f7ed]: https://github.com/abix-/TimberbornMods/commit/e65f7ed

## [v0.6.0] (2026-03-24)

- [breaking] Buildings and beavers default to compact output (use detail:full for all fields)
- [breaking] watch command renamed to top
- [feature] Event-driven entity indexes via EventBus ([`22e1ef4`][22e1ef4])
- [feature] Double-buffered caching, all GET requests on background thread ([`0dea90b`][0dea90b], [`4582b96`][4582b96])
- [feature] Cadenced cache refresh with settings.json config ([`17469fa`][17469fa])
- [feature] Detail param on buildings and beavers (basic/full/id:<id>) ([`79ccde1`][79ccde1])
- [feature] effectRadius on ranged effect buildings (monuments)
- [feature] productionProgress and readyToProduce on manufactories
- [feature] Per-good inventory, recipes, liftingCapacity on beavers ([`c55ed30`][c55ed30])
- [feature] Resource projection: logDays, plankDays, gearDays ([`f0a3ccf`][f0a3ccf])
- [feature] settings.json: refreshIntervalSeconds, debugEndpointEnabled, httpPort
- [fix] Pause/unpause uses game methods for proper UI icon ([`57e7323`][57e7323])
- [fix] Unemployed count uses adults only ([`f8b8bd2`][f8b8bd2])
- [fix] Double-buffer race condition on entity add/remove ([`f9a3ffe`][f9a3ffe])
- [fix] Shared reference-type fields between buffers ([`e781c3e`][e781c3e])
- [fix] Map occupant checks for new array format ([`71b8ebf`][71b8ebf])
- [internal] Cached component refs, eliminate GetComponent per request ([`a8bfc58`][a8bfc58], [`cf64b52`][cf64b52])
- [internal] RefChanged helper, building coords to add-time ([`0a6ab2f`][0a6ab2f])
- [internal] DoubleBuffer\<T\> generic, JsonWriter helper ([`daf384d`][daf384d])
- [internal] 247 integration tests, 2000/2000 reliability across 20 endpoints

[v0.6.0]: https://github.com/abix-/TimberbornMods/releases/tag/v0.6.0
[22e1ef4]: https://github.com/abix-/TimberbornMods/commit/22e1ef4
[0dea90b]: https://github.com/abix-/TimberbornMods/commit/0dea90b
[4582b96]: https://github.com/abix-/TimberbornMods/commit/4582b96
[a8bfc58]: https://github.com/abix-/TimberbornMods/commit/a8bfc58
[cf64b52]: https://github.com/abix-/TimberbornMods/commit/cf64b52
[17469fa]: https://github.com/abix-/TimberbornMods/commit/17469fa
[0a6ab2f]: https://github.com/abix-/TimberbornMods/commit/0a6ab2f
[daf384d]: https://github.com/abix-/TimberbornMods/commit/daf384d
[79ccde1]: https://github.com/abix-/TimberbornMods/commit/79ccde1
[f0a3ccf]: https://github.com/abix-/TimberbornMods/commit/f0a3ccf
[c55ed30]: https://github.com/abix-/TimberbornMods/commit/c55ed30
[15f5bcc]: https://github.com/abix-/TimberbornMods/commit/15f5bcc
[7a758bf]: https://github.com/abix-/TimberbornMods/commit/7a758bf
[57e7323]: https://github.com/abix-/TimberbornMods/commit/57e7323
[f8b8bd2]: https://github.com/abix-/TimberbornMods/commit/f8b8bd2
[f9a3ffe]: https://github.com/abix-/TimberbornMods/commit/f9a3ffe
[e781c3e]: https://github.com/abix-/TimberbornMods/commit/e781c3e
[71b8ebf]: https://github.com/abix-/TimberbornMods/commit/71b8ebf

## [v0.5.5] (2026-03-24)

- [feature] Building material costs and unlock status on prefabs endpoint
- [feature] Per-building stock and capacity for tanks, warehouses, stockpiles
- [feature] Available recipes and current recipe on manufactories
- [feature] Breeding pod nutrient status
- [feature] Beaver activity from game status system
- [feature] Clutch engage/disengage endpoint
- [feature] Per-beaver need breakdown (every unmet need by name)
- [feature] find_planting endpoint (valid irrigated spots within farmhouse range or area)
- [feature] building_range endpoint (work radius for farmhouse, lumberjack, forester, gatherer, scavenger, DC)
- [internal] 118 integration tests

[v0.5.5]: https://github.com/abix-/TimberbornMods/releases/tag/v0.5.5

## [v0.5.3] (2026-03-24)

- [breaking] Compass names for orientation everywhere (south, west, north, east)
- [breaking] Remove number and single-letter orientation fallbacks
- [feature] PATH setup for timberbot.py CLI

[v0.5.3]: https://github.com/abix-/TimberbornMods/releases/tag/v0.5.3

## [v0.5.2] (2026-03-24)

- [feature] Wellbeing breakdown endpoint (per-category: Social, Fun, Nutrition, Aesthetics, Awe)
- [fix] Building placement for water buildings (SwimmingPool, DoubleShower)
- [fix] Crop planting validation to match player UI behavior
- [fix] Placement on dead standing trees no longer incorrectly allowed
- [internal] 91 integration tests covering all endpoints

[v0.5.2]: https://github.com/abix-/TimberbornMods/releases/tag/v0.5.2

## [v0.5.1] (2026-03-23)

- [fix] unlock_building deducting science twice
- [feature] Wellbeing and worker management rules in AI prompt
- [feature] Water storage and drought preparation guidance
- [fix] Test coordinates to avoid gameplay-contaminated areas

[v0.5.1]: https://github.com/abix-/TimberbornMods/releases/tag/v0.5.1

## [v0.5.0] (2026-03-23)

- [feature] find_placement scans area for valid building spots with district reachability, path access, power adjacency
- [feature] place_path auto-builds stairs and platforms for multi-level z-changes
- [feature] summary includes foodDays and waterDays resource projections
- [feature] map returns moist field for irrigated tiles
- [feature] PlaceBuilding validates stackable blocks (platforms) as valid build surfaces
- [feature] Generic debug endpoint for inspecting game internals via reflection
- [fix] Crash when reloading a save (HTTP server port conflict)
- [internal] 81 automated tests covering all endpoints

[v0.5.0]: https://github.com/abix-/TimberbornMods/releases/tag/v0.5.0

## [v0.4.8] (2026-03-23)

- [feature] Terrain height shading on map tiles (darker = lower, lighter = higher)
- [feature] Empty ground displays z-level digit (ones place) instead of dots
- [feature] Height legend at bottom when multiple z-levels in view
- [internal] Test suite dynamically finds flat test area, skips locked buildings

[v0.4.8]: https://github.com/abix-/TimberbornMods/releases/tag/v0.4.8

## [v0.4.7] (2026-03-23)

- [feature] --json flag for full JSON output alongside default TOON format
- [feature] Summary shows housing, employment, wellbeing, science, and alerts in one call
- [feature] Hauler priority, manufactory recipes, farmhouse planting priority, forester tree priority endpoints
- [feature] Alerts, tree clusters, and scan run server-side
- [fix] Building unlock now works reliably in game UI for all buildings
- [fix] Names cleaned in all output (no more Clone/IronTeeth suffixes)
- [fix] Critical needs count only shows truly low needs
- [internal] 88 automated tests covering both output formats

[v0.4.7]: https://github.com/abix-/TimberbornMods/releases/tag/v0.4.7

## [v0.4.6] (2026-03-22)

- [feature] Badwater detection: badwater field on water tiles (0-1 contamination scale)
- [feature] Soil contamination: contaminated field on land tiles near badwater
- [feature] Dead trees (stumps) no longer block placement, show as .dead in scan
- [feature] Beaver workplace in TOON output
- [fix] Reject placement when building would clip underground (z must match terrain height)

[v0.4.6]: https://github.com/abix-/TimberbornMods/releases/tag/v0.4.6

## [v0.4.5] (2026-03-22)

- [fix] Science endpoint returns all 126 unlockable buildings with name, cost, and unlock status
- [feature] unlock_building updates game UI toolbar immediately
- [feature] Placing locked buildings blocked with error showing science cost needed
- [internal] 72 tests including placement unlock validation

[v0.4.5]: https://github.com/abix-/TimberbornMods/releases/tag/v0.4.5

## [v0.4.4] (2026-03-22)

- [fix] Separate constructionPriority (while building) and workplacePriority (when finished)
- [feature] set_priority accepts type:workplace or type:construction
- [internal] Renamed mod to "Timberbot API" in manifest

[v0.4.4]: https://github.com/abix-/TimberbornMods/releases/tag/v0.4.4

## [v0.4.3] (2026-03-22)

- [feature] Soil contamination on map tiles (contaminated field for badwater-affected ground)
- [feature] Per-building power (nominalPowerInput/nominalPowerOutput)
- [feature] District migration: migrate from:"District 1" to:"District 2" count:3
- [feature] Dwelling occupants (dwellers/maxDwellers on housing)
- [feature] Clutch status (isClutch/clutchEngaged on clutch buildings)
- [feature] Beaver home field (hasHome)
- [feature] Wellbeing tiers in TOON output (miserable/unhappy/okay/happy/ecstatic)
- [feature] Tree planting confirmed working (plant_crop crop:Pine)
- [breaking] Removed vanilla API wrappers -- Timberbot is complementary to built-in HTTP API
- [internal] 67 tests, GPL-3.0 license

[v0.4.3]: https://github.com/abix-/TimberbornMods/releases/tag/v0.4.3

## [v0.4.2] (2026-03-22)

- [feature] Pagination on buildings, trees, gatherables, beavers (limit/offset)
- [feature] isBot field on beavers (true for mechanical bots)
- [feature] contaminated field on beavers (badwater status)
- [feature] isWonder/wonderActive fields on buildings
- [feature] Work schedule read/write (workhours, set_workhours)
- [internal] 60 tests covering all read + write endpoints

[v0.4.2]: https://github.com/abix-/TimberbornMods/releases/tag/v0.4.2

## [v0.4.1] (2026-03-22)

- [feature] Construction progress on buildings (buildProgress, materialProgress, hasMaterials)
- [feature] Inventory contents on buildings
- [feature] Workplace assignment on beavers
- [feature] Notifications endpoint
- [feature] Alerts helper (finds unstaffed, unpowered, unreachable buildings)

[v0.4.1]: https://github.com/abix-/TimberbornMods/releases/tag/v0.4.1

## [v0.4.0] (2026-03-22)

- [feature] Science points endpoint, unlock buildings via API
- [feature] Distribution read/write, set import/export per good per district
- [feature] Reachable, powered, power network fields on buildings
- [feature] Tree cluster finder for optimal lumberjack placement
- [feature] AI playbook included (docs/timberbot.md), works as Claude Code /timberbot skill
- [fix] Beaver needs filter only shows active needs
- [fix] Speed scale matches game UI (0=pause, 1=normal, 2=fast, 3=fastest)

[v0.4.0]: https://github.com/abix-/TimberbornMods/releases/tag/v0.4.0

## [v0.3.8] (2026-03-22)

- [feature] CLI outputs compact TOON format (saves tokens for AI usage)
- [feature] Beavers command shows wellbeing and critical needs
- [feature] Scan and map output is tabular
- [breaking] Requires pip install toons (falls back to JSON if missing)

[v0.3.8]: https://github.com/abix-/TimberbornMods/releases/tag/v0.3.8

## [v0.3.7] (2026-03-22)

- [breaking] Named orientations -- use south/west/north/east instead of numbers
- [fix] Orientation origin correction: building stays in same spot when rotating
- [internal] 39 tests

[v0.3.7]: https://github.com/abix-/TimberbornMods/releases/tag/v0.3.7

## [v0.3.6] (2026-03-22)

- [fix] Crop planting validation -- skips invalid tiles instead of planting everywhere
- [fix] Crop marks skip buildings, water, and invalid terrain
- [internal] Added regression test suite (test_validation.py)

[v0.3.6]: https://github.com/abix-/TimberbornMods/releases/tag/v0.3.6

## [v0.3.5] (2026-03-22)

- [feature] C#-side placement validation -- checks all tiles before placing
- [feature] Occupancy, water, terrain, and off-map checks for every tile in footprint
- [feature] Orientation-aware footprint computation for multi-tile buildings
- [fix] Demolition debris no longer blocks placement
- [breaking] Removed redundant python-side validation

[v0.3.5]: https://github.com/abix-/TimberbornMods/releases/tag/v0.3.5

## [v0.3.4] (2026-03-22)

- [feature] TOON format scan output for AI token efficiency
- [feature] Colored roguelike visual command for humans
- [feature] Unique crop type letters (k=kohlrabi, c=carrot, etc)
- [feature] ANSI colored map with intuitive color families

[v0.3.4]: https://github.com/abix-/TimberbornMods/releases/tag/v0.3.4

## [v0.3.3] (2026-03-22)

- [feature] Summary endpoint includes tree stats (marked grown, marked seedlings, unmarked grown)

[v0.3.3]: https://github.com/abix-/TimberbornMods/releases/tag/v0.3.3

## [v0.3.2] (2026-03-22)

- [feature] Full building footprints on map scan (shows all tiles, not just origin)
- [feature] Seedling vs grown tree distinction on scan (T vs t)
- [feature] Building entrance coordinates (orientation, entranceX/Y/Z)
- [fix] Planting uses PlantingService.SetPlantingCoordinates (crops now appear in-game)
- [fix] Building placement validation for water buildings
- [feature] timberbot.py included in mod distribution and Workshop download

[v0.3.2]: https://github.com/abix-/TimberbornMods/releases/tag/v0.3.2

## [v0.3.1] (2026-03-22)

- [feature] timberbot.py included in release ZIP

[v0.3.1]: https://github.com/abix-/TimberbornMods/releases/tag/v0.3.1
