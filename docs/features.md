# Features

| System | What you can do | Status |
|---|---|---|
| [Beavers](api-reference.md#get-apibeavers) | Per-beaver wellbeing, every unmet need by name, workplace, age, contamination | Yes |
| [Wellbeing](api-reference.md#get-apiwellbeing) | Population breakdown by category with current/max -- know exactly what to build | Yes |
| [Buildings](api-reference.md#get-apibuildings) | Workers, priority, power, reachability, inventory, construction progress | Yes |
| [Placement](api-reference.md#post-apibuildingplace) | Place any building. Game-native validation. Find best spot with reachability + power | Yes |
| [Building range](api-reference.md#post-apibuildingrange) | Work radius for farmhouse, lumberjack, forester, gatherer, scavenger, DC | Yes |
| [Paths](api-reference.md#post-apipathroute) | Auto-stairs + platforms across z-levels | Yes |
| [Crops](api-reference.md#post-apiplantingmark) | Plant, clear, find valid irrigated spots within farmhouse range | Yes |
| [Trees](api-reference.md#get-apitrees) | Growth, cutting marks, densest clusters for lumberjack placement | Yes |
| [Map](api-reference.md#post-apimap) | Terrain, water, badwater, moisture, occupants. [ASCII visual](api-reference.md#post-apivisual) with height shading | Yes |
| [Weather](api-reference.md#get-apiweather) | Drought countdown, badtide status | Yes |
| [Power](api-reference.md#get-apibuildings) | Per-building input/output, generator/consumer, powered state | Yes |
| [Science](api-reference.md#get-apiscience) | Points, costs, unlock with one call | Yes |
| [Workers](api-reference.md#post-apiworkers) | Count, priority, pause, haul priority | Yes |
| [Distribution](api-reference.md#get-apidistribution) | Import/export per good, migrate beavers between districts | Yes |
| [Production](api-reference.md#post-apirecipe) | Recipes, farmhouse action, forester priority | Yes |
| [Summary](api-reference.md#get-apisummary) | Entire colony in one call | Yes |
| [Debug](api-reference.md#post-apidebug) | Reflect on any game object at runtime | Yes |
| Bot condition/fuel | Bot needs via beavers endpoint | In-Progress |
| Resource projection | Project wood/plank/gear days | Planned |

## By design

| System | Why not in Timberbot |
|---|---|
| Automation | Use Timberborn's built-in HTTP API (port 8080) |
| Logic gates | In-game only |
