Token optimization (~50% reduction on tiles, ~26% across all endpoints):
- [breaking] map: x/y/radius -> x1/y1/x2/y2
- [breaking] booleans: true/false -> 0/1 everywhere
- [breaking] uniform schema: all list endpoints always emit all fields (enables toon CSV)
- [breaking] tiles occupants: z-range format (DistrictCenter:z2-6), moved to last column

- [feature] brain: live summary + persistent goal/tasks/maps. Summary never persisted (always fresh). `brain goal:"text"` sets persistent objective. Auto-creates with DC map on first run
- [feature] summary: all brain fields server-side -- settlement, faction, DC per district, building role counts, treeClusters, foodClusters, per-district housing/employment/wellbeing, tree/crop species breakdowns, wellbeing categories
- [feature] DC coords per district instead of global (multi-district support)
- [feature] flat resource values per district (total stock, not available/all)
- [feature] per-district wellbeing (average, miserable, critical)
- [feature] cluster species breakdown (tree_clusters and food_clusters include per-species counts)
- [feature] food_clusters endpoint: grid-clustered gatherable food near DC
- [feature] settlement endpoint: lightweight save name for per-settlement memory
- [feature] --host= and --port= CLI flags for remote connections, httpHost in settings.json
- [feature] per-settlement memory folders (memory/{settlement}/)
- [feature] clear_brain: wipe settlement memory and start fresh
- [feature] map name param: saves ANSI map to memory and indexes in brain
- [feature] map delta ANSI: 35KB -> 6KB
- [feature] find_placement distance: path cost from DC via flow field
- [feature] summary: speed field

- [fix] localhost DNS -> 127.0.0.1 (2300ms -> 310ms latency)
- [fix] session reuse: 200x brain speedup
- [fix] toon summary aggregates population/resources across districts

- [internal] uniform schema + 0/1 booleans documented in architecture.md
