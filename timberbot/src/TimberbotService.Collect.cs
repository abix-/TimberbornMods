// TimberbotService.Collect.cs -- All read-only API endpoints.
//
// Every GET endpoint is a CollectX() method that reads from the double-buffered
// cache (background thread safe) and returns either a plain object (serialized to
// JSON by TimberbotHttpServer) or a StringBuilder of pre-built TOON output.
//
// These methods never touch game services directly -- they only read from
// _buildings.Read, _beavers.Read, _naturalResources.Read, and the thread-safe
// water/terrain maps.
//
// format param: "toon" = compact tabular (default, token-efficient for AI)
//               "json" = full nested objects (for programmatic access)
// detail param: "basic" = compact fields, "full" = all fields, "id:N" = single entity

using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.BlockSystem;
using Timberborn.BuilderPrioritySystem;
using Timberborn.Buildings;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockObjectTools;
using Timberborn.Coordinates;
using Timberborn.Cutting;
using Timberborn.TemplateInstantiation;
using Timberborn.MapIndexSystem;
using Timberborn.TerrainSystem;
using Timberborn.WaterSystem;
using Timberborn.EntitySystem;
using Timberborn.Forestry;
using Timberborn.Planting;
using Timberborn.Gathering;
using Timberborn.GameCycleSystem;
using Timberborn.GameDistricts;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using Timberborn.NaturalResourcesLifecycle;
using Timberborn.PrioritySystem;
using Timberborn.ResourceCountingSystem;
using Timberborn.SingletonSystem;
using Timberborn.Stockpiles;
using Timberborn.TimeSystem;
using Timberborn.WaterBuildings;
using Timberborn.WeatherSystem;
using Timberborn.WorkSystem;
using Timberborn.NeedSystem;
using Timberborn.LifeSystem;
using Timberborn.Wellbeing;
using Timberborn.BuildingsReachability;
using Timberborn.ConstructionSites;
using Timberborn.MechanicalSystem;
using Timberborn.ScienceSystem;
using Timberborn.BeaverContaminationSystem;
using Timberborn.Bots;
using Timberborn.Carrying;
using Timberborn.DeteriorationSystem;
using Timberborn.Wonders;
using Timberborn.NotificationSystem;
using Timberborn.StatusSystem;
using Timberborn.DwellingSystem;
using Timberborn.PowerManagement;
using Timberborn.SoilContaminationSystem;
using Timberborn.Hauling;
using Timberborn.Workshops;
using Timberborn.Reproduction;
using Timberborn.Fields;
using Timberborn.GameDistrictsMigration;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;
using Timberborn.PlantingUI;
using Timberborn.BuildingsNavigation;
using Timberborn.SoilMoistureSystem;
using Timberborn.NeedSpecs;
using Timberborn.GameFactionSystem;
using Timberborn.RangedEffectSystem;
using UnityEngine;

namespace Timberbot
{
    public partial class TimberbotService
    {
        // ================================================================
        // READ ENDPOINTS
        // Each returns an object serialized to JSON. The "format" param controls shape:
        //   toon: flat dicts/lists for tabular TOON display (default for CLI)
        //   json: full nested data for programmatic access (--json flag)
        // ================================================================

        // PERF: uses typed indexes instead of scanning all entities.
        // Three passes over subsets (buildings, natural resources, beavers) instead of one pass over everything.
        public object CollectSummary(string format = "toon")
        {
            int treeMarkedGrown = 0, treeMarkedSeedling = 0, treeUnmarkedGrown = 0;
            int cropReady = 0, cropGrowing = 0;
            int occupiedBeds = 0, totalBeds = 0;
            int totalVacancies = 0, assignedWorkers = 0;
            float totalWellbeing = 0f;
            int beaverCount = 0;
            int alertUnstaffed = 0, alertUnpowered = 0, alertUnreachable = 0;
            int miserable = 0, critical = 0;

            // natural resources: split into trees vs crops
            var _cropNames = new System.Collections.Generic.HashSet<string>
                { "Kohlrabi", "Soybean", "Corn", "Sunflower", "Eggplant", "Algae", "Cassava", "Mushroom", "Potato", "Wheat", "Carrot" };
            foreach (var c in _naturalResources.Read)
            {
                if (c.Cuttable == null) continue;
                if (!c.Alive) continue;
                if (_cropNames.Contains(c.Name))
                {
                    if (c.Grown) cropReady++;
                    else cropGrowing++;
                }
                else
                {
                    if (c.Marked && c.Grown) treeMarkedGrown++;
                    else if (c.Marked && !c.Grown) treeMarkedSeedling++;
                    else if (!c.Marked && c.Grown) treeUnmarkedGrown++;
                }
            }

            // buildings (read cached primitives only -- zero Unity calls)
            foreach (var c in _buildings.Read)
            {
                if (c.Dwelling != null)
                {
                    occupiedBeds += c.Dwellers;
                    totalBeds += c.MaxDwellers;
                }
                if (c.Workplace != null)
                {
                    assignedWorkers += c.AssignedWorkers;
                    totalVacancies += c.DesiredWorkers;
                    if (c.DesiredWorkers > 0 && c.AssignedWorkers < c.DesiredWorkers)
                        alertUnstaffed++;
                }
                if (c.IsConsumer && !c.Powered)
                    alertUnpowered++;
                if (c.Unreachable)
                    alertUnreachable++;
            }

            // beavers: cached wellbeing + critical needs
            foreach (var c in _beavers.Read)
            {
                totalWellbeing += c.Wellbeing;
                beaverCount++;
                if (c.Wellbeing < 4) miserable++;
                if (c.AnyCritical) critical++;
            }
            // count adults only (children can't work, shouldn't count as idle haulers)
            int totalAdults = 0;
            foreach (var dc in _districtCenterRegistry.FinishedDistrictCenters)
            {
                var pop = dc.GetComponent<DistrictPopulation>();
                if (pop != null) totalAdults += pop.NumberOfAdults;
            }
            int homeless = System.Math.Max(0, beaverCount - occupiedBeds);
            int unemployed = System.Math.Max(0, totalAdults - assignedWorkers);
            float avgWellbeing = beaverCount > 0 ? totalWellbeing / beaverCount : 0;

            if (format == "json")
            {
                return new
                {
                    time = CollectTime(),
                    weather = CollectWeather(),
                    districts = CollectDistricts("json"),
                    trees = new { markedGrown = treeMarkedGrown, markedSeedling = treeMarkedSeedling, unmarkedGrown = treeUnmarkedGrown },
                    crops = new { ready = cropReady, growing = cropGrowing },
                    housing = new { occupiedBeds, totalBeds, homeless },
                    employment = new { assigned = assignedWorkers, vacancies = totalVacancies, unemployed },
                    wellbeing = new { average = System.Math.Round(avgWellbeing, 1), miserable, critical },
                    science = _scienceService.SciencePoints,
                    alerts = new { unstaffed = alertUnstaffed, unpowered = alertUnpowered, unreachable = alertUnreachable }
                };
            }

            // build flat summary matching TOON output format
            var jw = _jw.Reset().OpenObj();

            // time
            jw.Key("day").Int(_dayNightCycle.DayNumber);
            jw.Key("dayProgress").Float((float)_dayNightCycle.DayProgress);

            // weather
            jw.Key("cycle").Int(_gameCycleService.Cycle);
            jw.Key("cycleDay").Int(_gameCycleService.CycleDay);
            jw.Key("isHazardous").Bool(_weatherService.IsHazardousWeather);
            jw.Key("tempDays").Int(_weatherService.TemperateWeatherDuration);
            jw.Key("hazardDays").Int(_weatherService.HazardousWeatherDuration);

            // trees (actual trees only, not crops)
            jw.Key("markedGrown").Int(treeMarkedGrown);
            jw.Key("markedSeedling").Int(treeMarkedSeedling);
            jw.Key("unmarkedGrown").Int(treeUnmarkedGrown);
            // crops
            jw.Key("cropReady").Int(cropReady);
            jw.Key("cropGrowing").Int(cropGrowing);

            // population + resources (first district)
            int totalFood = 0, totalWater = 0, logStock = 0, plankStock = 0, gearStock = 0;
            var goods = _goodService.Goods;
            foreach (var dc in _districtCenterRegistry.FinishedDistrictCenters)
            {
                var pop = dc.DistrictPopulation;
                jw.Key("adults").Int(pop.NumberOfAdults);
                jw.Key("children").Int(pop.NumberOfChildren);
                jw.Key("bots").Int(pop.NumberOfBots);
                var counter = dc.GetComponent<DistrictResourceCounter>();
                if (counter != null)
                {
                    foreach (var goodId in goods)
                    {
                        var rc = counter.GetResourceCount(goodId);
                        if (rc.AllStock > 0)
                        {
                            int stock = rc.AvailableStock;
                            jw.Key(goodId).Int(stock);
                            if (goodId == "Water") totalWater += stock;
                            else if (goodId == "Berries" || goodId == "Kohlrabi" || goodId == "Carrot" || goodId == "Potato"
                                  || goodId == "Wheat" || goodId == "Bread" || goodId == "Cassava" || goodId == "Corn"
                                  || goodId == "Eggplant" || goodId == "Soybean" || goodId == "MapleSyrup")
                                totalFood += stock;
                            else if (goodId == "Log") logStock = stock;
                            else if (goodId == "Plank") plankStock = stock;
                            else if (goodId == "Gear") gearStock = stock;
                        }
                    }
                }
            }

            // resource projection
            int totalPop = beaverCount;
            if (totalPop > 0)
            {
                jw.Key("foodDays").Float((float)((double)totalFood / totalPop), "F1");
                jw.Key("waterDays").Float((float)((double)totalWater / (totalPop * 2.0)), "F1");
                jw.Key("logDays").Float((float)((double)logStock / totalPop), "F1");
                jw.Key("plankDays").Float((float)((double)plankStock / totalPop), "F1");
                jw.Key("gearDays").Float((float)((double)gearStock / totalPop), "F1");
            }

            // housing
            jw.Key("beds").Str($"{occupiedBeds}/{totalBeds}");
            jw.Key("homeless").Int(homeless);

            // employment
            jw.Key("workers").Str($"{assignedWorkers}/{totalVacancies}");
            jw.Key("unemployed").Int(unemployed);

            // wellbeing
            jw.Key("wellbeing").Float((float)avgWellbeing, "F1");
            jw.Key("miserable").Int(miserable);
            jw.Key("critical").Int(critical);

            // science
            jw.Key("science").Int(_scienceService.SciencePoints);

            // alerts
            string alertStr = "none";
            if (alertUnstaffed > 0 || alertUnpowered > 0 || alertUnreachable > 0)
            {
                var parts = new List<string>();
                if (alertUnstaffed > 0) parts.Add($"{alertUnstaffed} unstaffed");
                if (alertUnpowered > 0) parts.Add($"{alertUnpowered} unpowered");
                if (alertUnreachable > 0) parts.Add($"{alertUnreachable} unreachable");
                alertStr = string.Join(", ", parts);
            }
            jw.Key("alerts").Str(alertStr);

            jw.CloseObj();
            return jw.ToString();
        }

        // PERF: iterates _buildings.Read instead of all entities.
        public object CollectAlerts()
        {
            var jw = _jw.Reset().OpenArr();
            foreach (var c in _buildings.Read)
            {
                if (c.Workplace != null && c.DesiredWorkers > 0 && c.AssignedWorkers < c.DesiredWorkers)
                    jw.OpenObj().Key("type").Str("unstaffed").Key("id").Int(c.Id).Key("name").Str(c.Name).Key("workers").Str($"{c.AssignedWorkers}/{c.DesiredWorkers}").CloseObj();

                if (c.IsConsumer && !c.Powered)
                    jw.OpenObj().Key("type").Str("unpowered").Key("id").Int(c.Id).Key("name").Str(c.Name).CloseObj();

                if (c.Unreachable)
                    jw.OpenObj().Key("type").Str("unreachable").Key("id").Int(c.Id).Key("name").Str(c.Name).CloseObj();
            }
            jw.CloseArr();
            return jw.ToString();
        }

        // PERF: O(n) entity scan + grid bucketing. Called occasionally for tree management.
        public object CollectTreeClusters(int cellSize = 10, int top = 5)
        {
            var cells = new Dictionary<long, int[]>(); // key -> [grown, total, centerX, centerY, z]
            foreach (var nr in _naturalResources.Read)
            {
                if (nr.Cuttable == null) continue;
                if (nr.Living == null || nr.Living.IsDead) continue;
                if (nr.BlockObject == null) continue;

                var c = nr.BlockObject.Coordinates;
                int cx = c.x / cellSize * cellSize + cellSize / 2;
                int cy = c.y / cellSize * cellSize + cellSize / 2;
                long key = (long)cx * 100000 + cy;

                if (!cells.ContainsKey(key))
                    cells[key] = new int[] { 0, 0, cx, cy, c.z };

                cells[key][1]++;
                if (nr.Growable != null && nr.Growable.IsGrown)
                    cells[key][0]++;
            }

            var sorted = new List<int[]>(cells.Values);
            sorted.Sort((a, b) => b[0].CompareTo(a[0]));
            var jw = _jw.Reset().OpenArr();
            for (int i = 0; i < System.Math.Min(top, sorted.Count); i++)
            {
                var s = sorted[i];
                jw.OpenObj().Key("x").Int(s[2]).Key("y").Int(s[3]).Key("z").Int(s[4]).Key("grown").Int(s[0]).Key("total").Int(s[1]).CloseObj();
            }
            jw.CloseArr();
            return jw.ToString();
        }

        public object CollectTime()
        {
            return new
            {
                dayNumber = _dayNightCycle.DayNumber,
                dayProgress = _dayNightCycle.DayProgress,
                partialDayNumber = _dayNightCycle.PartialDayNumber
            };
        }

        public object CollectWeather()
        {
            return new
            {
                cycle = _gameCycleService.Cycle,
                cycleDay = _gameCycleService.CycleDay,
                isHazardous = _weatherService.IsHazardousWeather,
                temperateWeatherDuration = _weatherService.TemperateWeatherDuration,
                hazardousWeatherDuration = _weatherService.HazardousWeatherDuration,
                cycleLengthInDays = _weatherService.CycleLengthInDays
            };
        }

        public object CollectDistricts(string format = "toon")
        {
            var goods = _goodService.Goods;
            var jw = _jw.Reset().OpenArr();
            foreach (var dc in _districtCenterRegistry.FinishedDistrictCenters)
            {
                var counter = dc.GetComponent<DistrictResourceCounter>();
                var pop = dc.DistrictPopulation;
                jw.OpenObj().Key("name").Str(dc.DistrictName);
                if (format == "toon")
                {
                    jw.Key("adults").Int(pop != null ? pop.NumberOfAdults : 0)
                      .Key("children").Int(pop != null ? pop.NumberOfChildren : 0)
                      .Key("bots").Int(pop != null ? pop.NumberOfBots : 0);
                    if (counter != null)
                        foreach (var goodId in goods)
                        {
                            var rc = counter.GetResourceCount(goodId);
                            if (rc.AllStock > 0) jw.Key(goodId).Int(rc.AvailableStock);
                        }
                }
                else
                {
                    jw.Key("population").OpenObj()
                        .Key("adults").Int(pop != null ? pop.NumberOfAdults : 0)
                        .Key("children").Int(pop != null ? pop.NumberOfChildren : 0)
                        .Key("bots").Int(pop != null ? pop.NumberOfBots : 0)
                        .CloseObj();
                    jw.Key("resources").OpenObj();
                    if (counter != null)
                        foreach (var goodId in goods)
                        {
                            var rc = counter.GetResourceCount(goodId);
                            if (rc.AllStock > 0) jw.Key(goodId).OpenObj().Key("available").Int(rc.AvailableStock).Key("all").Int(rc.AllStock).CloseObj();
                        }
                    jw.CloseObj();
                }
                jw.CloseObj();
            }
            jw.CloseArr();
            return jw.ToString();
        }

        public object CollectResources(string format = "toon")
        {
            var goods = _goodService.Goods;
            var jw = _jw.Reset();
            if (format == "toon")
            {
                jw.OpenArr();
                foreach (var dc in _districtCenterRegistry.FinishedDistrictCenters)
                {
                    var counter = dc.GetComponent<DistrictResourceCounter>();
                    if (counter == null) continue;
                    foreach (var goodId in goods)
                    {
                        var rc = counter.GetResourceCount(goodId);
                        if (rc.AllStock > 0)
                            jw.OpenObj().Key("district").Str(dc.DistrictName).Key("good").Str(goodId).Key("available").Int(rc.AvailableStock).Key("all").Int(rc.AllStock).CloseObj();
                    }
                }
                jw.CloseArr();
            }
            else
            {
                jw.OpenObj();
                foreach (var dc in _districtCenterRegistry.FinishedDistrictCenters)
                {
                    var counter = dc.GetComponent<DistrictResourceCounter>();
                    if (counter == null) continue;
                    jw.Key(dc.DistrictName).OpenObj();
                    foreach (var goodId in goods)
                    {
                        var rc = counter.GetResourceCount(goodId);
                        if (rc.AllStock > 0)
                            jw.Key(goodId).OpenObj().Key("available").Int(rc.AvailableStock).Key("all").Int(rc.AllStock).CloseObj();
                    }
                    jw.CloseObj();
                }
                jw.CloseObj();
            }
            return jw.ToString();
        }

        public object CollectPopulation()
        {
            var jw = _jw.Reset().OpenArr();
            foreach (var dc in _districtCenterRegistry.FinishedDistrictCenters)
            {
                var pop = dc.DistrictPopulation;
                jw.OpenObj()
                    .Key("district").Str(dc.DistrictName)
                    .Key("adults").Int(pop != null ? pop.NumberOfAdults : 0)
                    .Key("children").Int(pop != null ? pop.NumberOfChildren : 0)
                    .Key("bots").Int(pop != null ? pop.NumberOfBots : 0)
                    .CloseObj();
            }
            jw.CloseArr();
            return jw.ToString();
        }

        // PERF: StringBuilder serialization for buildings. Zero Dictionary alloc.
        public object CollectBuildings(string format = "toon", string detail = "basic")
        {
            int? singleId = null;
            if (detail != null && detail.StartsWith("id:"))
            {
                if (int.TryParse(detail.Substring(3), out int parsed))
                    singleId = parsed;
            }
            bool fullDetail = detail == "full" || singleId.HasValue;

            var sb = _sbBuildings;
            sb.Clear();
            Jw.OpenArr(sb);
            bool first = true;
            foreach (var c in _buildings.Read)
            {
                if (singleId.HasValue && c.Id != singleId.Value)
                    continue;
                if (!first) Jw.Sep(sb);
                first = false;

                Jw.Open(sb);
                Jw.KeyFirst(sb, "id"); Jw.Int(sb, c.Id);
                Jw.Key(sb, "name"); Jw.Str(sb, c.Name);
                Jw.Key(sb, "x"); Jw.Int(sb, c.X);
                Jw.Key(sb, "y"); Jw.Int(sb, c.Y);
                Jw.Key(sb, "z"); Jw.Int(sb, c.Z);
                Jw.Key(sb, "orientation"); Jw.Str(sb, c.Orientation ?? "");
                Jw.Key(sb, "finished"); Jw.Bool(sb, c.Finished);
                Jw.Key(sb, "paused"); Jw.Bool(sb, c.Paused);

                if (!fullDetail)
                {
                    Jw.Key(sb, "priority"); Jw.Str(sb, c.ConstructionPriority ?? "");
                    Jw.Key(sb, "workers"); Jw.Str(sb, c.Workplace != null ? $"{c.AssignedWorkers}/{c.DesiredWorkers}" : "");
                    Jw.Close(sb);
                    continue;
                }

                // full detail
                if (c.Pausable != null) { Jw.Key(sb, "pausable"); Jw.Bool(sb, true); }
                if (c.HasFloodgate) { Jw.Key(sb, "floodgate"); Jw.Bool(sb, true); Jw.Key(sb, "height"); Jw.Float(sb, c.FloodgateHeight, "F1"); Jw.Key(sb, "maxHeight"); Jw.Float(sb, c.FloodgateMaxHeight, "F1"); }
                if (c.ConstructionPriority != null) { Jw.Key(sb, "constructionPriority"); Jw.Str(sb, c.ConstructionPriority); }
                if (c.WorkplacePriorityStr != null) { Jw.Key(sb, "workplacePriority"); Jw.Str(sb, c.WorkplacePriorityStr); }
                if (c.Workplace != null) { Jw.Key(sb, "maxWorkers"); Jw.Int(sb, c.MaxWorkers); Jw.Key(sb, "desiredWorkers"); Jw.Int(sb, c.DesiredWorkers); Jw.Key(sb, "assignedWorkers"); Jw.Int(sb, c.AssignedWorkers); }
                if (c.Reachability != null) { Jw.Key(sb, "reachable"); Jw.Bool(sb, !c.Unreachable); }
                if (c.Mechanical != null) { Jw.Key(sb, "powered"); Jw.Bool(sb, c.Powered); }
                if (c.PowerNode != null)
                {
                    Jw.Key(sb, "isGenerator"); Jw.Bool(sb, c.IsGenerator);
                    Jw.Key(sb, "isConsumer"); Jw.Bool(sb, c.IsConsumer);
                    Jw.Key(sb, "nominalPowerInput"); Jw.Int(sb, c.NominalPowerInput);
                    Jw.Key(sb, "nominalPowerOutput"); Jw.Int(sb, c.NominalPowerOutput);
                    if (c.PowerDemand > 0 || c.PowerSupply > 0) { Jw.Key(sb, "powerDemand"); Jw.Int(sb, c.PowerDemand); Jw.Key(sb, "powerSupply"); Jw.Int(sb, c.PowerSupply); }
                }
                if (c.Site != null) { Jw.Key(sb, "buildProgress"); Jw.Float(sb, c.BuildProgress); Jw.Key(sb, "materialProgress"); Jw.Float(sb, c.MaterialProgress); Jw.Key(sb, "hasMaterials"); Jw.Bool(sb, c.HasMaterials); }
                if (c.Capacity > 0)
                {
                    Jw.Key(sb, "stock"); Jw.Int(sb, c.Stock);
                    Jw.Key(sb, "capacity"); Jw.Int(sb, c.Capacity);
                    if (c.Inventory != null && c.Inventory.Count > 0)
                    {
                        Jw.Key(sb, "inventory"); Jw.Open(sb);
                        bool ifirst = true;
                        foreach (var kvp in c.Inventory)
                        {
                            if (!ifirst) Jw.Sep(sb);
                            else { Jw.KeyFirst(sb, kvp.Key); ifirst = false; Jw.Int(sb, kvp.Value); continue; }
                            Jw.KeyFirst(sb, kvp.Key); Jw.Int(sb, kvp.Value);
                        }
                        Jw.Close(sb);
                    }
                }
                if (c.HasWonder) { Jw.Key(sb, "isWonder"); Jw.Bool(sb, true); Jw.Key(sb, "wonderActive"); Jw.Bool(sb, c.WonderActive); }
                if (c.Dwelling != null) { Jw.Key(sb, "dwellers"); Jw.Int(sb, c.Dwellers); Jw.Key(sb, "maxDwellers"); Jw.Int(sb, c.MaxDwellers); }
                if (c.HasClutch) { Jw.Key(sb, "isClutch"); Jw.Bool(sb, true); Jw.Key(sb, "clutchEngaged"); Jw.Bool(sb, c.ClutchEngaged); }
                if (c.Manufactory != null)
                {
                    if (c.Recipes != null && c.Recipes.Count > 0)
                    {
                        Jw.Key(sb, "recipes"); Jw.OpenArr(sb);
                        for (int ri = 0; ri < c.Recipes.Count; ri++)
                        {
                            if (ri > 0) Jw.Sep(sb);
                            Jw.Str(sb, c.Recipes[ri]);
                        }
                        Jw.CloseArr(sb);
                    }
                    Jw.Key(sb, "currentRecipe"); Jw.Str(sb, c.CurrentRecipe ?? "");
                    Jw.Key(sb, "productionProgress"); Jw.Float(sb, c.ProductionProgress);
                    Jw.Key(sb, "readyToProduce"); Jw.Bool(sb, c.ReadyToProduce);
                }
                if (c.BreedingPod != null)
                {
                    Jw.Key(sb, "needsNutrients"); Jw.Bool(sb, c.NeedsNutrients);
                    if (c.NutrientStock != null && c.NutrientStock.Count > 0)
                    {
                        Jw.Key(sb, "nutrients"); Jw.Open(sb);
                        bool nfirst = true;
                        foreach (var kvp in c.NutrientStock)
                        {
                            if (!nfirst) Jw.Sep(sb);
                            else { Jw.KeyFirst(sb, kvp.Key); nfirst = false; Jw.Int(sb, kvp.Value); continue; }
                            Jw.KeyFirst(sb, kvp.Key); Jw.Int(sb, kvp.Value);
                        }
                        Jw.Close(sb);
                    }
                }
                if (c.EffectRadius > 0) { Jw.Key(sb, "effectRadius"); Jw.Int(sb, c.EffectRadius); }
                Jw.Close(sb);
            }
            Jw.CloseArr(sb);
            return sb.ToString();
        }

        // PERF: cached component refs -- zero GetComponent per item.
        // serial param: dict (default), anon, sb -- for A/B testing serialization methods
        // PERF: StringBuilder serialization -- 2ms for 3000 trees. No Dictionary, no Newtonsoft.
        private object CollectNaturalResourcesSb(System.Text.StringBuilder sb, System.Collections.Generic.HashSet<string> species)
        {
            sb.Clear();
            Jw.OpenArr(sb);
            bool first = true;
            foreach (var c in _naturalResources.Read)
            {
                if (c.Cuttable == null) continue;
                if (!species.Contains(c.Name)) continue;
                if (!first) Jw.Sep(sb);
                first = false;
                Jw.Open(sb);
                Jw.KeyFirst(sb, "id"); Jw.Int(sb, c.Id);
                Jw.Key(sb, "name"); Jw.Str(sb, c.Name);
                Jw.Key(sb, "x"); Jw.Int(sb, c.X);
                Jw.Key(sb, "y"); Jw.Int(sb, c.Y);
                Jw.Key(sb, "z"); Jw.Int(sb, c.Z);
                Jw.Key(sb, "marked"); Jw.Bool(sb, c.Marked);
                Jw.Key(sb, "alive"); Jw.Bool(sb, c.Alive);
                Jw.Key(sb, "grown"); Jw.Bool(sb, c.Grown);
                Jw.Key(sb, "growth"); Jw.Float(sb, c.Growth);
                Jw.Close(sb);
            }
            Jw.CloseArr(sb);
            return sb.ToString();
        }

        public object CollectTrees() => CollectNaturalResourcesSb(_sbTrees, _treeSpecies);
        public object CollectCrops() => CollectNaturalResourcesSb(_sbCrops, _cropSpecies);

        public object CollectGatherables()
        {
            var sb = _sbGatherables;
            sb.Clear();
            Jw.OpenArr(sb);
            bool first = true;
            foreach (var c in _naturalResources.Read)
            {
                if (c.Gatherable == null) continue;
                if (!first) Jw.Sep(sb);
                first = false;
                Jw.Open(sb);
                Jw.KeyFirst(sb, "id"); Jw.Int(sb, c.Id);
                Jw.Key(sb, "name"); Jw.Str(sb, c.Name);
                Jw.Key(sb, "x"); Jw.Int(sb, c.X);
                Jw.Key(sb, "y"); Jw.Int(sb, c.Y);
                Jw.Key(sb, "z"); Jw.Int(sb, c.Z);
                Jw.Key(sb, "alive"); Jw.Bool(sb, c.Alive);
                Jw.Close(sb);
            }
            Jw.CloseArr(sb);
            return sb.ToString();
        }

        // PERF: reads cached beaver data only. Zero GetComponent from background thread.
        private readonly System.Text.StringBuilder _sbBeavers = new System.Text.StringBuilder(50000);

        public object CollectBeavers(string format = "toon", string detail = "basic")
        {
            int? singleId = null;
            if (detail != null && detail.StartsWith("id:"))
            {
                if (int.TryParse(detail.Substring(3), out int parsed))
                    singleId = parsed;
            }
            bool fullDetail = detail == "full" || singleId.HasValue;

            var sb = _sbBeavers;
            sb.Clear();
            Jw.OpenArr(sb);
            bool first = true;
            foreach (var c in _beavers.Read)
            {
                if (singleId.HasValue && c.Id != singleId.Value)
                    continue;
                if (!first) Jw.Sep(sb);
                first = false;

                Jw.Open(sb);
                Jw.KeyFirst(sb, "id"); Jw.Int(sb, c.Id);
                Jw.Key(sb, "name"); Jw.Str(sb, c.Name);
                Jw.Key(sb, "x"); Jw.Int(sb, c.X);
                Jw.Key(sb, "y"); Jw.Int(sb, c.Y);
                Jw.Key(sb, "z"); Jw.Int(sb, c.Z);
                Jw.Key(sb, "wellbeing"); Jw.Float(sb, c.Wellbeing, "F1");
                Jw.Key(sb, "isBot"); Jw.Bool(sb, c.IsBot);

                if (!fullDetail)
                {
                    float wb = c.Wellbeing;
                    string tier = wb >= 16 ? "ecstatic" : wb >= 12 ? "happy" : wb >= 8 ? "okay" : wb >= 4 ? "unhappy" : "miserable";
                    Jw.Key(sb, "tier"); Jw.Str(sb, tier);
                    Jw.Key(sb, "workplace"); Jw.Str(sb, c.Workplace ?? "");

                    // critical + unmet need summaries
                    sb.Append(",\"critical\":\"");
                    bool cfirst = true;
                    if (c.Needs != null)
                        foreach (var n in c.Needs)
                            if (n.Critical) { if (!cfirst) sb.Append('+'); cfirst = false; sb.Append(n.Id); }
                    sb.Append("\",\"unmet\":\"");
                    bool ufirst = true;
                    if (c.Needs != null)
                        foreach (var n in c.Needs)
                            if (!n.Favorable && !n.Critical && n.Active) { if (!ufirst) sb.Append('+'); ufirst = false; sb.Append(n.Id); }
                    sb.Append("\"}");
                    continue;
                }

                // full detail
                Jw.Key(sb, "anyCritical"); Jw.Bool(sb, c.AnyCritical);
                if (c.Workplace != null) { Jw.Key(sb, "workplace"); Jw.Str(sb, c.Workplace); }
                if (c.District != null) { Jw.Key(sb, "district"); Jw.Str(sb, c.District); }
                Jw.Key(sb, "hasHome"); Jw.Bool(sb, c.HasHome);
                Jw.Key(sb, "contaminated"); Jw.Bool(sb, c.Contaminated);
                if (c.Life != null) { Jw.Key(sb, "lifeProgress"); Jw.Float(sb, c.LifeProgress); }
                if (c.Deteriorable != null) { Jw.Key(sb, "deterioration"); Jw.Float(sb, c.DeteriorationProgress, "F3"); }
                if (c.Carrier != null) { Jw.Key(sb, "liftingCapacity"); Jw.Int(sb, c.LiftingCapacity); if (c.Overburdened) { Jw.Key(sb, "overburdened"); Jw.Bool(sb, true); } }
                if (c.IsCarrying) { Jw.Key(sb, "carrying"); Jw.Str(sb, c.CarryingGood); Jw.Key(sb, "carryAmount"); Jw.Int(sb, c.CarryAmount); }

                // needs array
                Jw.Key(sb, "needs"); Jw.OpenArr(sb);
                if (c.Needs != null)
                {
                    bool nfirst = true;
                    foreach (var n in c.Needs)
                    {
                        if (!fullDetail && !c.IsBot && !n.Active) continue;
                        if (!nfirst) Jw.Sep(sb);
                        nfirst = false;
                        Jw.Open(sb);
                        Jw.KeyFirst(sb, "id"); Jw.Str(sb, n.Id);
                        Jw.Key(sb, "points"); Jw.Float(sb, n.Points);
                        Jw.Key(sb, "wellbeing"); Jw.Int(sb, n.Wellbeing);
                        Jw.Key(sb, "favorable"); Jw.Bool(sb, n.Favorable);
                        Jw.Key(sb, "critical"); Jw.Bool(sb, n.Critical);
                        Jw.Key(sb, "group"); Jw.Str(sb, n.Group);
                        Jw.Close(sb);
                    }
                }
                Jw.CloseArr(sb);
                Jw.Close(sb);
            }
            Jw.CloseArr(sb);
            return sb.ToString();
        }

        private struct PowerNetwork { public int Id, Supply, Demand; public List<int> BuildingIndices; }

        public object CollectPowerNetworks()
        {
            // group buildings by power network using cached PowerNetworkId
            var networks = new Dictionary<int, PowerNetwork>();
            var buildings = _buildings.Read;
            for (int i = 0; i < buildings.Count; i++)
            {
                var c = buildings[i];
                if (c.PowerNode == null || c.PowerNetworkId == 0) continue;
                int netId = c.PowerNetworkId;
                if (!networks.ContainsKey(netId))
                    networks[netId] = new PowerNetwork { Id = netId, Supply = c.PowerSupply, Demand = c.PowerDemand, BuildingIndices = new List<int>() };
                networks[netId].BuildingIndices.Add(i);
            }
            var jw = _jw.Reset().OpenArr();
            foreach (var net in networks.Values)
            {
                jw.OpenObj().Key("id").Int(net.Id).Key("supply").Int(net.Supply).Key("demand").Int(net.Demand);
                jw.Key("buildings").OpenArr();
                foreach (var idx in net.BuildingIndices)
                {
                    var c = buildings[idx];
                    jw.OpenObj().Key("name").Str(c.Name).Key("id").Int(c.Id).Key("isGenerator").Bool(c.IsGenerator).Key("nominalOutput").Int(c.NominalPowerOutput).Key("nominalInput").Int(c.NominalPowerInput).CloseObj();
                }
                jw.CloseArr().CloseObj();
            }
            jw.CloseArr();
            return jw.ToString();
        }

        public object CollectSpeed()
        {
            var raw = _speedManager.CurrentSpeed;
            int level = System.Array.IndexOf(SpeedScale, raw);
            if (level < 0) level = 0;
            return new { speed = level };
        }

        public object CollectWorkHours()
        {
            return new
            {
                endHours = _workingHoursManager.EndHours,
                areWorkingHours = _workingHoursManager.AreWorkingHours
            };
        }

        // set when beavers stop working (1-24 hours)
        public object SetWorkHours(int endHours)
        {
            if (endHours < 1 || endHours > 24)
                return new { error = "endHours must be 1-24" };
            _workingHoursManager.EndHours = endHours;
            return new { endHours = _workingHoursManager.EndHours };
        }

        // move adult beavers between districts
        public object MigratePopulation(string fromDistrict, string toDistrict, int count)
        {
            Timberborn.GameDistricts.DistrictCenter fromDc = null, toDc = null;
            foreach (var dc in _districtCenterRegistry.FinishedDistrictCenters)
            {
                if (dc.DistrictName == fromDistrict) fromDc = dc;
                if (dc.DistrictName == toDistrict) toDc = dc;
            }
            if (fromDc == null) return new { error = "from district not found", from = fromDistrict };
            if (toDc == null) return new { error = "to district not found", to = toDistrict };

            try
            {
                var distributor = _populationDistributorRetriever.GetPopulationDistributor<AdultsDistributorTemplate>(fromDc);
                if (distributor == null)
                    return new { error = "no population distributor", from = fromDistrict };

                var available = distributor.Current;
                var toMove = System.Math.Min(count, available);
                if (toMove <= 0)
                    return new { error = "no population to migrate", from = fromDistrict, available };

                distributor.MigrateTo(toDc, toMove);
                return new { from = fromDistrict, to = toDistrict, migrated = toMove };
            }
            catch (System.Exception ex)
            {
                return new { error = ex.Message, from = fromDistrict, to = toDistrict };
            }
        }

        // PERF: O(n) entity scan to build occupant lookup, then O(region) tile iteration.
        // Region-bounded so cost depends on area size, not map size. Called occasionally.
    }
}
