# A* Path Building with Stair Placement -- Current State

## What we're building

`place_path` routes a path from (x1,y1) to (x2,y2) across a Timberborn map. The path crosses multiple elevation levels. At each z-change, stairs must be placed. The goal is the SHORTEST path with LOWEST cost -- one A* pass, no wasted tiles.

## How stairs work in Timberborn

- Stairs are 1x1 buildings placed on the LOWER tile of a z-change edge
- They have an entrance side (lower) and exit side (higher)
- Orientation determines which direction is "uphill": south(0), west(1), north(2), east(3)
- The entrance tile (one tile before the stair, same z as stair) must have an adjacent path
- The exit tile (one tile after the stair, higher z) must have an adjacent path
- Beavers can ONLY enter from the entrance side -- wrong orientation = impassable
- Chaining stairs is fine: exit of one stair can be the entrance of the next

## The diagonal problem

A* on a 4-directional grid produces staircase patterns on diagonal routes (alternating +X and +Y steps). When the path hits a z-change edge, we need to pick a cardinal direction for the stair. The old approach used the single A* step before the z-change -- but on a diagonal staircase, that step is arbitrarily +X or +Y. After several sections, the stair could face sideways, making the entrance inaccessible.

### Failed fix 1: Lookback
Look back 5 waypoints to find "dominant" direction. Problem: the dominant direction didn't match the actual A* path at the z-change point, so the stair entrance was offset from the path, requiring extra detour paths to reach it. Made paths LONGER, not shorter.

### Failed fix 2: Destination-based direction
Orient stairs toward the destination (dominant cardinal axis from z-change to goal). Problem: the stair tile ended up on the wrong terrain. E.g., z-change is between x=168 (z=8) and x=169 (z=9). Destination says go +Y. Stair tile computed at (169,166) which has z=9, but baseZ=8. Terrain conflict.

### Current approach: Pre-computed stair edges in A* graph (partially working)

Based on standard game dev approach (Unity forums, Red Blob Games): model stairs as directed portal edges IN the A* graph, not computed post-hoc.

**How it works:**
1. `BuildCostGrid` scans every z-change edge in the terrain
2. For each single-level z-change, checks if a stair can physically be placed (lower tile + entrance tile unobstructed, same z)
3. If valid, makes that edge traversable with cost=20 (instead of 255/impassable) and stores the stair info (tile, orientation, entrance, exit) in a lookup dictionary keyed by (fromIdx, toIdx)
4. A* finds a route through these stair edges naturally -- no ambiguity about orientation since it's determined by which edge was crossed
5. Walking the A* result: flat tiles get paths, z-change edges place stairs from the lookup

**What works:**
- 1600-1900 stair edges are found in the grid (validation is working)
- A* finds routes through z-change edges (the cost=20 edges are traversable)
- First stair crossing places correctly (section 1 works)
- Stair orientation is inherently correct (determined by the edge direction)

**What's broken:**
- `stoppedAt` reports the entrance tile, not the exit tile. The next section starts from the entrance (z=2 side) instead of the exit (z=3 side), so it can't find a route forward
- The A* heuristic was changed from greedy (f = h*4) to proper A* (f = g + h*2) to handle cost=20 edges, but this may need tuning
- Multi-level stairs (levels > 1 requiring platforms) are marked impassable -- not handled yet
- The `sections` param counts stair crossings and stops, but the stopped position needs to be the EXIT tile of the last stair

## Key files

- `timberbot/src/TimberbotPlacement.cs`
  - `RoutePath()` -- main entry point, walks A* result and places paths/stairs
  - `BuildCostGrid()` -- builds edge-based cost grid with pre-computed stair edges
  - `AStarPath()` -- 4-directional A* with edge-based costs
  - `StairEdge` struct -- pre-computed stair info (tile, z, orientation, entrance, exit)
- `timberbot/src/TimberbotHttpServer.cs` -- routes POST /api/path/place to RoutePath
- `timberbot/script/timberbot.py`
  - `place_path()` -- Python client for the API
  - `map()` -- ASCII map renderer (fixed: now shows topmost occupant by z)
  - `_launch()` -- game launcher (fixed: now kills existing Timberborn before launching)

## Edge-based cost grid layout

`ushort[w * h * 4]` -- 4 entry costs per tile.

Direction indices: 0=from west(+X), 1=from east(-X), 2=from south(+Y), 3=from north(-Y).

The neighbor offset arrays `ndx/ndy` point to where the neighbor IS (not the travel direction):
- d=0: ndx=-1, ndy=0 (neighbor to the west)
- d=1: ndx=+1, ndy=0 (neighbor to the east)
- d=2: ndx=0, ndy=-1 (neighbor to the south)
- d=3: ndx=0, ndy=+1 (neighbor to the north)

`grid[idx*4 + d]` = cost of entering tile idx from neighbor at ndx[d],ndy[d].

A* reads `grid[nidx * 4 + opposite[d]]` when stepping in direction d to tile nidx. `opposite = {1,0,3,2}`.

## Stair edge computation

For each tile (lx,ly) and each neighbor direction d:
- If `heights[idx] != heights[nIdx]` (z-change):
  - Travel direction: from neighbor to this tile = `(-ndx[d], -ndy[d])`
  - goingUp: `heights[idx] > heights[nIdx]` (this tile higher than neighbor)
  - Stair tile = lower of the two tiles
  - Entrance = one tile back from stair in travel direction (must be same z as stair, unobstructed)
  - Exit = the higher tile
  - Orientation: uphill direction mapped to orient index
  - Edge key: `(nIdx, idx)` -- A* steps FROM neighbor TO this tile

## What needs fixing

1. **stoppedAt position**: When `sections > 0` and we stop after N stair crossings, `stoppedAt` must report the stair EXIT tile (higher z), not the entrance. The next invocation starts from stoppedAt.

2. **Multi-level stairs**: Currently marked impassable. Need to check if platforms are unlocked, then model multi-level ramps as a sequence of stair+platform placements with higher cost.

3. **A* heuristic tuning**: Changed from greedy to proper A* to handle cost=20 stair edges. May need adjustment for performance on large grids.

4. **Edge direction consistency**: The ndx/ndy (neighbor offset) vs ddx/ddy (travel direction) confusion caused multiple bugs. The grid uses ndx/ndy convention. The A* uses ddx/ddy. The `opposite` array bridges them. This mapping is fragile and needs careful documentation or unification.

## Commits so far

- `92ee47b` a* path: orient stairs toward destination (reverted -- caused wrong z)
- `49afb70` map: show topmost occupant (highest z) for correct top-down view
- Uncommitted: pre-computed stair edges in A* graph, stair failure bailout, launch kills existing game, sections support

## Research sources

- [Unity multi-level pathfinding](https://forum.unity.com/threads/multi-level-height-pathfinding.373728/) -- model level connections as portal edges
- [Red Blob Games grid algorithms](https://www.redblobgames.com/pathfinding/grids/algorithms.html) -- edge-based costs, heuristic admissibility
- [Staircase mod](https://timberborn.thunderstore.io/package/KnatteAnka_And_Tobbert/Staircase/) -- alternative stair orientations
- [A* 3D grid pathfinding](https://answers.unity.com/questions/1096235/using-stairs-in-3d-grid-based-pathfinding-a.html) -- stairs as graph edges
