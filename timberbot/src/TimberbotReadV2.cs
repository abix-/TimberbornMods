using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Timberborn.BlockSystem;
using Timberborn.BuilderPrioritySystem;
using Timberborn.Buildings;
using Timberborn.BuildingsReachability;
using Timberborn.ConstructionSites;
using Timberborn.DwellingSystem;
using Timberborn.EntitySystem;
using Timberborn.GameFactionSystem;
using Timberborn.GameCycleSystem;
using Timberborn.GameDistricts;
using Timberborn.InventorySystem;
using Timberborn.MechanicalSystem;
using Timberborn.NeedSpecs;
using Timberborn.NotificationSystem;
using Timberborn.PowerManagement;
using Timberborn.PrioritySystem;
using Timberborn.RangedEffectSystem;
using Timberborn.Reproduction;
using Timberborn.ScienceSystem;
using Timberborn.SingletonSystem;
using Timberborn.StatusSystem;
using Timberborn.TimeSystem;
using Timberborn.WaterBuildings;
using Timberborn.WeatherSystem;
using Timberborn.Wonders;
using Timberborn.WorkSystem;
using Timberborn.Workshops;

namespace Timberbot
{
    // Native /api/v2 read stack. Keeps generic snapshot, route, and serialization
    // helpers internal so v2 stays DRY without adding extra top-level services.
    public class TimberbotReadV2
    {
        private readonly EntityRegistry _entityRegistry;
        private readonly EventBus _eventBus;
        private readonly GameCycleService _gameCycleService;
        private readonly WeatherService _weatherService;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly SpeedManager _speedManager;
        private readonly WorkingHoursManager _workingHoursManager;
        private readonly TimberbotEntityCache _cache;
        private readonly ScienceService _scienceService;
        private readonly BuildingService _buildingService;
        private readonly BuildingUnlockingService _buildingUnlockingService;
        private readonly DistrictCenterRegistry _districtCenterRegistry;
        private readonly FactionNeedService _factionNeedService;
        private readonly NotificationSaver _notificationSaver;
        private readonly List<TrackedBuildingRef> _tracked = new List<TrackedBuildingRef>();
        private readonly Dictionary<int, TrackedBuildingRef> _trackedById = new Dictionary<int, TrackedBuildingRef>();
        private readonly TimberbotJw _jw = new TimberbotJw(300000);
        private readonly StringBuilder _toonSb = new StringBuilder(256);
        private readonly TimberbotJw _scienceBuildJw = new TimberbotJw(4096);
        private readonly TimberbotJw _distributionBuildJw = new TimberbotJw(4096);
        private readonly ProjectionSnapshot<BuildingDefinition, BuildingState, BuildingDetailState> _snapshot
            = new ProjectionSnapshot<BuildingDefinition, BuildingState, BuildingDetailState>();
        private readonly CollectionRoute<BuildingDefinition, BuildingState, BuildingDetailState> _buildingsEndpoint;
        private readonly ValueStore<SettlementSnapshot> _settlementStore = new ValueStore<SettlementSnapshot>();
        private readonly ValueStore<TimeSnapshot> _timeStore = new ValueStore<TimeSnapshot>();
        private readonly ValueStore<WeatherSnapshot> _weatherStore = new ValueStore<WeatherSnapshot>();
        private readonly ValueStore<SpeedSnapshot> _speedStore = new ValueStore<SpeedSnapshot>();
        private readonly ValueStore<WorkHoursSnapshot> _workHoursStore = new ValueStore<WorkHoursSnapshot>();
        private readonly ValueStore<RawJsonSnapshot> _scienceStore = new ValueStore<RawJsonSnapshot>();
        private readonly ValueStore<RawJsonSnapshot> _distributionStore = new ValueStore<RawJsonSnapshot>();
        private readonly ValueStore<NotificationItem[]> _notificationsStore = new ValueStore<NotificationItem[]>();
        private readonly ValueRoute<TimeSnapshot> _timeRoute;
        private readonly ValueRoute<WeatherSnapshot> _weatherRoute;
        private readonly ValueRoute<SpeedSnapshot> _speedRoute;
        private readonly ValueRoute<WorkHoursSnapshot> _workHoursRoute;
        private readonly ValueRoute<RawJsonSnapshot> _scienceRoute;
        private readonly ValueRoute<RawJsonSnapshot> _distributionRoute;
        private readonly FlatArrayRoute<NotificationItem> _notificationsRoute;
        private readonly FlatArrayRoute<AlertItem> _alertsRoute;
        private readonly ValueRoute<PowerNetworkItem[]> _powerRoute;
        private readonly Dictionary<string, int[]> _treeSpecies = new Dictionary<string, int[]>();
        private readonly Dictionary<string, int[]> _cropSpecies = new Dictionary<string, int[]>();
        private readonly Dictionary<string, int> _roleCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, int[]> _districtStats = new Dictionary<string, int[]>();
        private readonly Dictionary<string, (int x, int y, int z, string orientation, int entranceX, int entranceY)> _districtDCs = new Dictionary<string, (int, int, int, string, int, int)>();
        private readonly Dictionary<string, string> _needToGroup = new Dictionary<string, string>();
        private readonly Dictionary<string, float> _groupMaxPerBeaver = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _wbGroupTotals = new Dictionary<string, float>();
        private readonly Dictionary<string, float[]> _districtWb = new Dictionary<string, float[]>();
        private readonly Dictionary<string, int> _resourceTotals = new Dictionary<string, int>();
        private readonly Dictionary<long, int[]> _clusterCells = new Dictionary<long, int[]>();
        private readonly Dictionary<long, Dictionary<string, int>> _clusterSpecies = new Dictionary<long, Dictionary<string, int>>();
        private readonly List<long> _clusterSorted = new List<long>();
        private static readonly HashSet<string> _cropNames = new HashSet<string>
            { "Kohlrabi", "Soybean", "Corn", "Sunflower", "Eggplant", "Algae", "Cassava", "Mushroom", "Potato", "Wheat", "Carrot" };
        private static readonly Dictionary<string, string[]> _roleMap = new Dictionary<string, string[]> {
            {"water", new[]{"Pump","Tank","FluidDump","AquiferDrill"}},
            {"food", new[]{"FarmHouse","AquaticFarmhouse","EfficientFarmHouse","Gatherer","Grill","Gristmill","Bakery","FoodFactory","Fermenter","HydroponicGarden"}},
            {"housing", new[]{"Lodge","MiniLodge","DoubleLodge","TripleLodge","Rowhouse","Barrack"}},
            {"wood", new[]{"Lumberjack","LumberMill","IndustrialLumberMill","Forester"}},
            {"storage", new[]{"Warehouse","Pile","ReservePile","ReserveWarehouse","ReserveTank"}},
            {"power", new[]{"PowerWheel","LargePowerWheel","WaterWheel","WindTurbine","LargeWindTurbine","SteamEngine","PowerShaft","Clutch","GravityBattery"}},
            {"science", new[]{"Inventor","Numbercruncher","Observatory"}},
            {"production", new[]{"GearWorkshop","Smelter","Metalsmith","Scavenger","Mine","BotAssembler","BotPartFactory","PaperMill","PrintingPress","WoodWorkshop","Centrifuge","ExplosivesFactory","Refinery"}},
            {"leisure", new[]{"Campfire","Scratcher","Shower","DoubleShower","SwimmingPool","Carousel","MudPit","MudBath","ExercisePlaza","WindTunnel","Motivatorium","Lido","Detailer","ContemplationSpot","Agora","DanceHall","RooftopTerrace","MedicalBed","TeethGrindstone","Herbalist","DecontaminationPod","ChargingStation"}},
        };

        public TimberbotReadV2(
            EntityRegistry entityRegistry,
            EventBus eventBus,
            GameCycleService gameCycleService,
            WeatherService weatherService,
            IDayNightCycle dayNightCycle,
            SpeedManager speedManager,
            WorkingHoursManager workingHoursManager,
            TimberbotEntityCache cache,
            ScienceService scienceService,
            BuildingService buildingService,
            BuildingUnlockingService buildingUnlockingService,
            DistrictCenterRegistry districtCenterRegistry,
            FactionNeedService factionNeedService,
            NotificationSaver notificationSaver)
        {
            _entityRegistry = entityRegistry;
            _eventBus = eventBus;
            _gameCycleService = gameCycleService;
            _weatherService = weatherService;
            _dayNightCycle = dayNightCycle;
            _speedManager = speedManager;
            _workingHoursManager = workingHoursManager;
            _cache = cache;
            _scienceService = scienceService;
            _buildingService = buildingService;
            _buildingUnlockingService = buildingUnlockingService;
            _districtCenterRegistry = districtCenterRegistry;
            _factionNeedService = factionNeedService;
            _notificationSaver = notificationSaver;
            _buildingsEndpoint = new CollectionRoute<BuildingDefinition, BuildingState, BuildingDetailState>(
                _jw,
                (fullDetail, timeoutMs) => _snapshot.RequestFresh(fullDetail, timeoutMs),
                new BuildingCollectionSchema());
            _timeRoute = new ValueRoute<TimeSnapshot>(
                new TimberbotJw(256),
                timeoutMs => _timeStore.RequestFresh(timeoutMs),
                new TimeSchema());
            _weatherRoute = new ValueRoute<WeatherSnapshot>(
                new TimberbotJw(256),
                timeoutMs => _weatherStore.RequestFresh(timeoutMs),
                new WeatherSchema());
            _speedRoute = new ValueRoute<SpeedSnapshot>(
                new TimberbotJw(64),
                timeoutMs => _speedStore.RequestFresh(timeoutMs),
                new SpeedSchema());
            _workHoursRoute = new ValueRoute<WorkHoursSnapshot>(
                new TimberbotJw(96),
                timeoutMs => _workHoursStore.RequestFresh(timeoutMs),
                new WorkHoursSchema());
            _scienceRoute = new ValueRoute<RawJsonSnapshot>(
                new TimberbotJw(8192),
                timeoutMs => _scienceStore.RequestFresh(timeoutMs),
                new RawJsonSchema());
            _distributionRoute = new ValueRoute<RawJsonSnapshot>(
                new TimberbotJw(8192),
                timeoutMs => _distributionStore.RequestFresh(timeoutMs),
                new RawJsonSchema());
            _notificationsRoute = new FlatArrayRoute<NotificationItem>(
                new TimberbotJw(8192),
                timeoutMs => _notificationsStore.RequestFresh(timeoutMs),
                new NotificationSchema());
            _alertsRoute = new FlatArrayRoute<AlertItem>(
                new TimberbotJw(8192),
                _ => BuildAlertsFromBuildings(),
                new AlertSchema());
            _powerRoute = new ValueRoute<PowerNetworkItem[]>(
                new TimberbotJw(8192),
                _ => BuildPowerFromBuildings(),
                new PowerSchema());
        }

        public int PublishSequence => _snapshot.Sequence;
        public int LastPublishedCount => _snapshot.Count;
        public float LastPublishedAt => _snapshot.PublishedAt;

        public void Register() => _eventBus.Register(this);
        public void Unregister() => _eventBus.Unregister(this);

        public void BuildAll()
        {
            _tracked.Clear();
            _trackedById.Clear();
            foreach (var ec in _entityRegistry.Entities)
                TryAddTracked(ec);
            _snapshot.MarkDirty();
            _snapshot.PublishNow(0f, _tracked.Count,
                i => _tracked[i].Definition,
                (s, i) => RefreshState(s, _tracked[i]));
            _settlementStore.PublishNow(0f, BuildSettlementSnapshot);
            _timeStore.PublishNow(0f, BuildTimeSnapshot);
            _weatherStore.PublishNow(0f, BuildWeatherSnapshot);
            _speedStore.PublishNow(0f, BuildSpeedSnapshot);
            _workHoursStore.PublishNow(0f, BuildWorkHoursSnapshot);
            _scienceStore.PublishNow(0f, BuildScienceSnapshot);
            _distributionStore.PublishNow(0f, BuildDistributionSnapshot);
            _notificationsStore.PublishNow(0f, BuildNotificationsSnapshot);
        }

        public void ProcessPendingRefresh(float now)
        {
            _snapshot.ProcessPending(now, _tracked.Count,
                i => _tracked[i].Definition,
                (s, i) => RefreshState(s, _tracked[i]),
                (d, i) => RefreshDetail(d, _tracked[i]));
            _settlementStore.ProcessPending(now, BuildSettlementSnapshot);
            _timeStore.ProcessPending(now, BuildTimeSnapshot);
            _weatherStore.ProcessPending(now, BuildWeatherSnapshot);
            _speedStore.ProcessPending(now, BuildSpeedSnapshot);
            _workHoursStore.ProcessPending(now, BuildWorkHoursSnapshot);
            _scienceStore.ProcessPending(now, BuildScienceSnapshot);
            _distributionStore.ProcessPending(now, BuildDistributionSnapshot);
            _notificationsStore.ProcessPending(now, BuildNotificationsSnapshot);
        }

        public object CollectBuildings(string format = "toon", string detail = "basic", int limit = 100, int offset = 0,
            string filterName = null, int filterX = 0, int filterY = 0, int filterRadius = 0)
            => _buildingsEndpoint.Collect(format, detail, limit, offset, filterName, filterX, filterY, filterRadius);

        public object CollectSummary(string format = "toon")
        {
            int treeMarkedGrown = 0, treeMarkedSeedling = 0, treeUnmarkedGrown = 0;
            int cropReady = 0, cropGrowing = 0;
            _treeSpecies.Clear();
            _cropSpecies.Clear();
            var treeSpecies = _treeSpecies;
            var cropSpecies = _cropSpecies;
            int occupiedBeds = 0, totalBeds = 0;
            int totalVacancies = 0, assignedWorkers = 0;
            float totalWellbeing = 0f;
            int beaverCount = 0;
            int alertUnstaffed = 0, alertUnpowered = 0, alertUnreachable = 0;
            int miserable = 0, critical = 0;

            foreach (var c in _cache.NaturalResources.Read)
            {
                if (c.Cuttable == null) continue;
                if (c.Alive == 0) continue;
                if (_cropNames.Contains(c.Name))
                {
                    if (c.Grown != 0) cropReady++; else cropGrowing++;
                    if (!cropSpecies.TryGetValue(c.Name, out var cs)) { cs = new int[2]; cropSpecies[c.Name] = cs; }
                    if (c.Grown != 0) cs[0]++; else cs[1]++;
                }
                else
                {
                    if (c.Marked != 0 && c.Grown != 0) treeMarkedGrown++;
                    else if (c.Marked != 0 && c.Grown == 0) treeMarkedSeedling++;
                    else if (c.Marked == 0 && c.Grown != 0) treeUnmarkedGrown++;
                    if (!treeSpecies.TryGetValue(c.Name, out var ts)) { ts = new int[3]; treeSpecies[c.Name] = ts; }
                    if (c.Marked != 0 && c.Grown != 0) ts[0]++;
                    else if (c.Marked == 0 && c.Grown != 0) ts[1]++;
                    else if (c.Marked != 0 && c.Grown == 0) ts[2]++;
                }
            }

            int dcX = 0, dcY = 0, dcZ = 0;
            bool foundDC = false;
            _roleCounts.Clear();
            _districtStats.Clear();
            _districtDCs.Clear();
            var roleCounts = _roleCounts;
            var districtStats = _districtStats;
            var districtDCs = _districtDCs;
            var roleMap = _roleMap;

            var buildingSnapshot = _snapshot.RequestFresh(false, 2000);
            for (int i = 0; i < buildingSnapshot.Count; i++)
            {
                var d = buildingSnapshot.Definitions[i];
                var c = buildingSnapshot.States[i];
                var dname = c.District ?? "_unknown";
                if (!districtStats.TryGetValue(dname, out var ds)) { ds = new int[7]; districtStats[dname] = ds; }
                occupiedBeds += c.Dwellers;
                totalBeds += c.MaxDwellers;
                ds[0] += c.Dwellers;
                ds[1] += c.MaxDwellers;
                assignedWorkers += c.AssignedWorkers;
                totalVacancies += c.DesiredWorkers;
                ds[2] += c.AssignedWorkers;
                ds[3] += c.DesiredWorkers;
                if (c.DesiredWorkers > 0 && c.AssignedWorkers < c.DesiredWorkers) { alertUnstaffed++; ds[4]++; }
                if (d.IsConsumer != 0 && c.Powered == 0) { alertUnpowered++; ds[5]++; }
                if (c.Unreachable != 0) { alertUnreachable++; ds[6]++; }
                if (d.Name != null && d.Name.Contains("DistrictCenter"))
                {
                    var dcOri = d.Orientation ?? "south";
                    int eX = d.X + 1, eY = d.Y + 1;
                    if (dcOri == "south") { eX = d.X + 1; eY = d.Y - 1; }
                    else if (dcOri == "north") { eX = d.X + 1; eY = d.Y + 3; }
                    else if (dcOri == "east") { eX = d.X + 3; eY = d.Y + 1; }
                    else if (dcOri == "west") { eX = d.X - 1; eY = d.Y + 1; }
                    districtDCs[dname] = (d.X, d.Y, d.Z, dcOri, eX, eY);
                    if (!foundDC) { foundDC = true; dcX = d.X; dcY = d.Y; dcZ = d.Z; }
                }
                string name = d.Name ?? "";
                if (name == "Path") { roleCounts["paths"] = roleCounts.GetValueOrDefault("paths") + 1; continue; }
                bool matched = false;
                foreach (var kv in roleMap)
                {
                    foreach (var keyword in kv.Value)
                    {
                        if (name.Contains(keyword)) { roleCounts[kv.Key] = roleCounts.GetValueOrDefault(kv.Key) + 1; matched = true; break; }
                    }
                    if (matched) break;
                }
                if (!matched) roleCounts["other"] = roleCounts.GetValueOrDefault("other") + 1;
            }

            string faction = TimberbotEntityCache.FactionSuffix == ".Folktails" ? "Folktails" : TimberbotEntityCache.FactionSuffix == ".IronTeeth" ? "IronTeeth" : "unknown";
            var beaverNeeds = _factionNeedService.GetBeaverNeeds();
            _needToGroup.Clear();
            _groupMaxPerBeaver.Clear();
            var needToGroup = _needToGroup;
            var groupMaxPerBeaver = _groupMaxPerBeaver;
            foreach (var ns in beaverNeeds)
            {
                if (string.IsNullOrEmpty(ns.NeedGroupId)) continue;
                needToGroup[ns.Id] = ns.NeedGroupId;
                groupMaxPerBeaver[ns.NeedGroupId] = groupMaxPerBeaver.GetValueOrDefault(ns.NeedGroupId) + ns.FavorableWellbeing;
            }
            _wbGroupTotals.Clear();
            _districtWb.Clear();
            var wbGroupTotals = _wbGroupTotals;
            var districtWb = _districtWb;
            foreach (var c in _cache.Beavers.Read)
            {
                totalWellbeing += c.Wellbeing;
                beaverCount++;
                if (c.Wellbeing < 4) miserable++;
                if (c.AnyCritical != 0) critical++;
                var bDist = c.District ?? "_unknown";
                if (!districtWb.TryGetValue(bDist, out var dw)) { dw = new float[4]; districtWb[bDist] = dw; }
                dw[0] += c.Wellbeing; dw[1]++; if (c.Wellbeing < 4) dw[2]++; if (c.AnyCritical != 0) dw[3]++;
                if (c.Needs != null)
                    foreach (var n in c.Needs)
                        if (needToGroup.ContainsKey(n.Id))
                            wbGroupTotals[needToGroup[n.Id]] = wbGroupTotals.GetValueOrDefault(needToGroup[n.Id]) + n.Wellbeing;
            }

            int totalAdults = 0, totalChildren = 0, totalBots = 0;
            foreach (var dc in _cache.Districts)
            { totalAdults += dc.Adults; totalChildren += dc.Children; totalBots += dc.Bots; }
            int homeless = Math.Max(0, beaverCount - occupiedBeds);
            int unemployed = Math.Max(0, totalAdults - assignedWorkers);
            float avgWellbeing = beaverCount > 0 ? totalWellbeing / beaverCount : 0;
            int currentSpeed = Array.IndexOf(TimberbotRead.SpeedScale, _speedManager.CurrentSpeed);
            if (currentSpeed < 0) currentSpeed = 0;

            if (format == "json")
            {
                var jj = _cache.Jw.BeginObj();
                jj.Prop("settlement", GetSettlementName());
                jj.Prop("faction", faction);
                jj.Obj("time").Prop("dayNumber", _dayNightCycle.DayNumber).Prop("dayProgress", (float)_dayNightCycle.DayProgress).Prop("partialDayNumber", (float)_dayNightCycle.PartialDayNumber).Prop("speed", currentSpeed).CloseObj();
                jj.Obj("weather").Prop("cycle", (int)_gameCycleService.Cycle).Prop("cycleDay", _gameCycleService.CycleDay).Prop("isHazardous", _weatherService.IsHazardousWeather).Prop("temperateWeatherDuration", _weatherService.TemperateWeatherDuration).Prop("hazardousWeatherDuration", _weatherService.HazardousWeatherDuration).Prop("cycleLengthInDays", _weatherService.CycleLengthInDays).CloseObj();
                jj.Arr("districts");
                foreach (var dc in _cache.Districts)
                {
                    jj.OpenObj().Prop("name", dc.Name);
                    jj.Obj("population").Prop("adults", dc.Adults).Prop("children", dc.Children).Prop("bots", dc.Bots).CloseObj();
                    jj.Obj("resources");
                    if (dc.Resources != null)
                        foreach (var kvp in dc.Resources) jj.Prop(kvp.Key, kvp.Value.all);
                    jj.CloseObj();
                    var ds = districtStats.GetValueOrDefault(dc.Name);
                    int dBeds = ds != null ? ds[1] : 0;
                    int dOccBeds = ds != null ? ds[0] : 0;
                    int dPop = dc.Adults + dc.Children + dc.Bots;
                    jj.Obj("housing").Prop("occupiedBeds", dOccBeds).Prop("totalBeds", dBeds).Prop("homeless", Math.Max(0, dPop - dOccBeds)).CloseObj();
                    int dAssigned = ds != null ? ds[2] : 0;
                    int dVacancies = ds != null ? ds[3] : 0;
                    jj.Obj("employment").Prop("assigned", dAssigned).Prop("vacancies", dVacancies).Prop("unemployed", Math.Max(0, dc.Adults - dAssigned)).CloseObj();
                    var dwb = districtWb.GetValueOrDefault(dc.Name);
                    float dAvgWb = dwb != null && dwb[1] > 0 ? dwb[0] / dwb[1] : 0;
                    jj.Obj("wellbeing").Prop("average", (float)Math.Round(dAvgWb, 1), "F1").Prop("miserable", (int)(dwb != null ? dwb[2] : 0)).Prop("critical", (int)(dwb != null ? dwb[3] : 0)).CloseObj();
                    if (districtDCs.TryGetValue(dc.Name, out var ddc))
                        jj.Obj("dc").Prop("x", ddc.x).Prop("y", ddc.y).Prop("z", ddc.z).Prop("orientation", ddc.orientation).Prop("entranceX", ddc.entranceX).Prop("entranceY", ddc.entranceY).CloseObj();
                    jj.CloseObj();
                }
                jj.CloseArr();
                jj.Obj("trees").Prop("markedGrown", treeMarkedGrown).Prop("markedSeedling", treeMarkedSeedling).Prop("unmarkedGrown", treeUnmarkedGrown);
                jj.Arr("species");
                foreach (var kv in treeSpecies)
                    jj.OpenObj().Prop("name", kv.Key).Prop("markedGrown", kv.Value[0]).Prop("unmarkedGrown", kv.Value[1]).Prop("seedling", kv.Value[2]).CloseObj();
                jj.CloseArr().CloseObj();
                jj.Obj("crops").Prop("ready", cropReady).Prop("growing", cropGrowing);
                jj.Arr("species");
                foreach (var kv in cropSpecies)
                    jj.OpenObj().Prop("name", kv.Key).Prop("ready", kv.Value[0]).Prop("growing", kv.Value[1]).CloseObj();
                jj.CloseArr().CloseObj();
                jj.Obj("wellbeing").Prop("average", avgWellbeing, "F1").Prop("miserable", miserable).Prop("critical", critical);
                jj.Arr("categories");
                foreach (var kv in groupMaxPerBeaver)
                {
                    float avg = beaverCount > 0 ? wbGroupTotals.GetValueOrDefault(kv.Key) / beaverCount : 0;
                    float max = kv.Value;
                    jj.OpenObj().Prop("group", kv.Key).Prop("current", (float)Math.Round(avg, 1), "F1").Prop("max", (float)Math.Round(max, 1), "F1").CloseObj();
                }
                jj.CloseArr().CloseObj();
                jj.Prop("science", _scienceService.SciencePoints);
                jj.Obj("alerts").Prop("unstaffed", alertUnstaffed).Prop("unpowered", alertUnpowered).Prop("unreachable", alertUnreachable).CloseObj();
                jj.Obj("buildings");
                foreach (var kv in roleCounts) jj.Prop(kv.Key, kv.Value);
                jj.CloseObj();
                WriteClustersFiltered(jj, "treeClusters", _cache.NaturalResources.Read, null, dcX, dcY, dcZ, 40, 10, 5);
                WriteClustersFiltered(jj, "foodClusters", _cache.NaturalResources.Read, TimberbotEntityCache.TreeSpecies, dcX, dcY, dcZ, 40, 10, 5);
                return jj.End();
            }

            var jw = _cache.Jw.BeginObj();
            jw.Prop("settlement", GetSettlementName());
            jw.Prop("faction", faction);
            jw.Prop("day", _dayNightCycle.DayNumber);
            jw.Prop("dayProgress", (float)_dayNightCycle.DayProgress);
            jw.Prop("speed", currentSpeed);
            jw.Prop("cycle", (int)_gameCycleService.Cycle);
            jw.Prop("cycleDay", _gameCycleService.CycleDay);
            jw.Prop("isHazardous", _weatherService.IsHazardousWeather);
            jw.Prop("tempDays", _weatherService.TemperateWeatherDuration);
            jw.Prop("hazardDays", _weatherService.HazardousWeatherDuration);
            jw.Prop("markedGrown", treeMarkedGrown);
            jw.Prop("markedSeedling", treeMarkedSeedling);
            jw.Prop("unmarkedGrown", treeUnmarkedGrown);
            jw.Prop("cropReady", cropReady);
            jw.Prop("cropGrowing", cropGrowing);
            int totalFood = 0, totalWater = 0, logStock = 0, plankStock = 0, gearStock = 0;
            _resourceTotals.Clear();
            var resourceTotals = _resourceTotals;
            foreach (var dc in _cache.Districts)
            {
                if (dc.Resources != null)
                {
                    foreach (var kvp in dc.Resources)
                    {
                        int avail = kvp.Value.available;
                        resourceTotals[kvp.Key] = resourceTotals.GetValueOrDefault(kvp.Key) + avail;
                        if (kvp.Key == "Water") totalWater += avail;
                        else if (kvp.Key == "Berries" || kvp.Key == "Kohlrabi" || kvp.Key == "Carrot" || kvp.Key == "Potato" || kvp.Key == "Wheat" || kvp.Key == "Bread" || kvp.Key == "Cassava" || kvp.Key == "Corn" || kvp.Key == "Eggplant" || kvp.Key == "Soybean" || kvp.Key == "MapleSyrup")
                            totalFood += avail;
                        else if (kvp.Key == "Log") logStock += avail;
                        else if (kvp.Key == "Plank") plankStock += avail;
                        else if (kvp.Key == "Gear") gearStock += avail;
                    }
                }
            }
            jw.Prop("adults", totalAdults);
            jw.Prop("children", totalChildren);
            jw.Prop("bots", totalBots);
            foreach (var kvp in resourceTotals)
                jw.Key(kvp.Key).Int(kvp.Value);
            int totalPop = beaverCount;
            if (totalPop > 0)
            {
                jw.Prop("foodDays", (float)((double)totalFood / totalPop), "F1");
                jw.Prop("waterDays", (float)((double)totalWater / (totalPop * 2.0)), "F1");
                jw.Prop("logDays", (float)((double)logStock / totalPop), "F1");
                jw.Prop("plankDays", (float)((double)plankStock / totalPop), "F1");
                jw.Prop("gearDays", (float)((double)gearStock / totalPop), "F1");
            }
            jw.Prop("beds", $"{occupiedBeds}/{totalBeds}");
            jw.Prop("homeless", homeless);
            jw.Prop("workers", $"{assignedWorkers}/{totalVacancies}");
            jw.Prop("unemployed", unemployed);
            jw.Prop("wellbeing", avgWellbeing, "F1");
            jw.Prop("miserable", miserable);
            jw.Prop("critical", critical);
            jw.Prop("science", _scienceService.SciencePoints);
            string alertStr = "none";
            if (alertUnstaffed > 0 || alertUnpowered > 0 || alertUnreachable > 0)
            {
                var parts = new List<string>();
                if (alertUnstaffed > 0) parts.Add($"{alertUnstaffed} unstaffed");
                if (alertUnpowered > 0) parts.Add($"{alertUnpowered} unpowered");
                if (alertUnreachable > 0) parts.Add($"{alertUnreachable} unreachable");
                alertStr = string.Join(", ", parts);
            }
            jw.Prop("alerts", alertStr);
            jw.Obj("buildings");
            foreach (var kv in roleCounts) jw.Prop(kv.Key, kv.Value);
            jw.CloseObj();
            return jw.End();
        }
        public object CollectAlerts(string format = "toon", int limit = 100, int offset = 0)
            => _alertsRoute.Collect(format, limit, offset);
        public object CollectTreeClusters(string format = "toon", int cellSize = 10, int top = 5)
        {
            _clusterCells.Clear(); _clusterSpecies.Clear();
            var cells = _clusterCells;
            var cellSpecies = _clusterSpecies;
            foreach (var nr in _cache.NaturalResources.Read)
            {
                if (nr.Cuttable == null) continue;
                if (nr.Alive == 0) continue;
                int cx = nr.X / cellSize * cellSize + cellSize / 2;
                int cy = nr.Y / cellSize * cellSize + cellSize / 2;
                long key = (long)cx * 100000 + cy;
                if (!cells.ContainsKey(key))
                { cells[key] = new int[] { 0, 0, cx, cy, nr.Z }; cellSpecies[key] = new Dictionary<string, int>(); }
                cells[key][1]++;
                cellSpecies[key][nr.Name] = cellSpecies[key].GetValueOrDefault(nr.Name) + 1;
                if (nr.Grown != 0) cells[key][0]++;
            }
            _clusterSorted.Clear(); _clusterSorted.AddRange(cells.Keys);
            _clusterSorted.Sort((a, b) => cells[b][0].CompareTo(cells[a][0]));
            var jw = _cache.Jw.BeginArr();
            for (int i = 0; i < Math.Min(top, _clusterSorted.Count); i++)
            {
                var s = cells[_clusterSorted[i]];
                jw.OpenObj().Prop("x", s[2]).Prop("y", s[3]).Prop("z", s[4]).Prop("grown", s[0]).Prop("total", s[1]);
                var sp = cellSpecies[_clusterSorted[i]];
                jw.Obj("species");
                foreach (var kv in sp) jw.Prop(kv.Key, kv.Value);
                jw.CloseObj().CloseObj();
            }
            return jw.End();
        }
        public object CollectFoodClusters(string format = "toon", int cellSize = 10, int top = 5)
        {
            _clusterCells.Clear(); _clusterSpecies.Clear();
            var cells = _clusterCells;
            var cellSpecies = _clusterSpecies;
            foreach (var nr in _cache.NaturalResources.Read)
            {
                if (nr.Gatherable == null) continue;
                if (TimberbotEntityCache.TreeSpecies.Contains(nr.Name)) continue;
                if (nr.Alive == 0) continue;
                int cx = nr.X / cellSize * cellSize + cellSize / 2;
                int cy = nr.Y / cellSize * cellSize + cellSize / 2;
                long key = (long)cx * 100000 + cy;
                if (!cells.ContainsKey(key))
                { cells[key] = new int[] { 0, 0, cx, cy, nr.Z }; cellSpecies[key] = new Dictionary<string, int>(); }
                cells[key][1]++;
                cellSpecies[key][nr.Name] = cellSpecies[key].GetValueOrDefault(nr.Name) + 1;
                if (nr.Grown != 0) cells[key][0]++;
            }
            _clusterSorted.Clear(); _clusterSorted.AddRange(cells.Keys);
            _clusterSorted.Sort((a, b) => cells[b][0].CompareTo(cells[a][0]));
            var jw = _cache.Jw.BeginArr();
            for (int i = 0; i < Math.Min(top, _clusterSorted.Count); i++)
            {
                var s = cells[_clusterSorted[i]];
                jw.OpenObj().Prop("x", s[2]).Prop("y", s[3]).Prop("z", s[4]).Prop("grown", s[0]).Prop("total", s[1]);
                var sp = cellSpecies[_clusterSorted[i]];
                jw.Obj("species");
                foreach (var kv in sp) jw.Prop(kv.Key, kv.Value);
                jw.CloseObj().CloseObj();
            }
            return jw.End();
        }
        public object CollectResources(string format = "toon")
        {
            var jw = _cache.Jw.Reset();
            if (format == "toon")
            {
                jw.OpenArr();
                foreach (var dc in _cache.Districts)
                {
                    if (dc.Resources == null) continue;
                    foreach (var kvp in dc.Resources)
                        jw.OpenObj().Prop("district", dc.Name).Prop("good", kvp.Key).Prop("available", kvp.Value.available).Prop("all", kvp.Value.all).CloseObj();
                }
                jw.CloseArr();
            }
            else
            {
                jw.OpenObj();
                foreach (var dc in _cache.Districts)
                {
                    jw.Key(dc.Name).OpenObj();
                    if (dc.ResourcesJson != null) jw.Raw(dc.ResourcesJson);
                    jw.CloseObj();
                }
                jw.CloseObj();
            }
            return jw.ToString();
        }
        public object CollectPopulation()
        {
            var jw = _cache.Jw.BeginArr();
            foreach (var dc in _cache.Districts)
            {
                jw.OpenObj()
                    .Prop("district", dc.Name)
                    .Prop("adults", dc.Adults)
                    .Prop("children", dc.Children)
                    .Prop("bots", dc.Bots)
                    .CloseObj();
            }
            return jw.End();
        }
        public object CollectTime() => _timeRoute.Collect();
        public object CollectWeather() => _weatherRoute.Collect();
        public object CollectDistricts(string format = "toon")
        {
            var jw = _cache.Jw.BeginArr();
            foreach (var dc in _cache.Districts)
            {
                jw.OpenObj().Prop("name", dc.Name);
                if (format == "toon")
                {
                    jw.Prop("adults", dc.Adults).Prop("children", dc.Children).Prop("bots", dc.Bots);
                    if (!string.IsNullOrEmpty(dc.ResourcesToon)) jw.Raw(dc.ResourcesToon);
                }
                else
                {
                    jw.Obj("population")
                        .Prop("adults", dc.Adults).Prop("children", dc.Children).Prop("bots", dc.Bots)
                        .CloseObj();
                    jw.Obj("resources");
                    if (dc.ResourcesJson != null) jw.Raw(dc.ResourcesJson);
                    jw.CloseObj();
                }
                jw.CloseObj();
            }
            return jw.End();
        }
        public object CollectTrees(string format = "toon", int limit = 100, int offset = 0, string filterName = null, int filterX = 0, int filterY = 0, int filterRadius = 0)
            => CollectNaturalResourcesJw(_cache.Jw, TimberbotEntityCache.TreeSpecies, limit, offset, filterName, filterX, filterY, filterRadius);
        public object CollectCrops(string format = "toon", int limit = 100, int offset = 0, string filterName = null, int filterX = 0, int filterY = 0, int filterRadius = 0)
            => CollectNaturalResourcesJw(_cache.Jw, TimberbotEntityCache.CropSpecies, limit, offset, filterName, filterX, filterY, filterRadius);
        public object CollectGatherables(string format = "toon", int limit = 100, int offset = 0, string filterName = null, int filterX = 0, int filterY = 0, int filterRadius = 0)
        {
            bool paginated = limit > 0;
            bool hasFilter = filterName != null || filterRadius > 0;
            int skipped = 0, emitted = 0, total = 0;
            if (paginated)
                foreach (var c in _cache.NaturalResources.Read)
                    if (c.Gatherable != null && (!hasFilter || PassesFilter(c.Name, c.X, c.Y, filterName, filterX, filterY, filterRadius))) total++;

            var jw = _cache.Jw.Reset();
            if (paginated) jw.OpenObj().Prop("total", total).Prop("offset", offset).Prop("limit", limit).Key("items");
            jw.OpenArr();
            foreach (var c in _cache.NaturalResources.Read)
            {
                if (c.Gatherable == null) continue;
                if (hasFilter && !PassesFilter(c.Name, c.X, c.Y, filterName, filterX, filterY, filterRadius)) continue;
                if (offset > 0 && skipped < offset) { skipped++; continue; }
                if (paginated && emitted >= limit) break;
                emitted++;
                jw.OpenObj()
                    .Prop("id", c.Id)
                    .Prop("name", c.Name)
                    .Prop("x", c.X).Prop("y", c.Y).Prop("z", c.Z)
                    .Prop("alive", c.Alive)
                    .CloseObj();
            }
            jw.CloseArr();
            if (paginated) jw.CloseObj();
            return jw.ToString();
        }
        public object CollectBeavers(string format = "toon", string detail = "basic", int limit = 100, int offset = 0, string filterName = null, int filterX = 0, int filterY = 0, int filterRadius = 0)
        {
            int? singleId = null;
            if (detail != null && detail.StartsWith("id:"))
            {
                if (int.TryParse(detail.Substring(3), out int parsed))
                    singleId = parsed;
            }
            bool fullDetail = detail == "full" || singleId.HasValue;
            bool hasFilter = filterName != null || filterRadius > 0;
            bool paginated = limit > 0 && !singleId.HasValue;
            int total = _cache.Beavers.Read.Count;
            if (paginated && hasFilter)
            {
                total = 0;
                foreach (var b in _cache.Beavers.Read)
                    if (PassesFilter(b.Name, b.X, b.Y, filterName, filterX, filterY, filterRadius)) total++;
            }
            int skipped = 0, emitted = 0;

            var jw = _cache.Jw.Reset();
            if (paginated) jw.OpenObj().Prop("total", total).Prop("offset", offset).Prop("limit", limit).Key("items");
            jw.OpenArr();
            foreach (var c in _cache.Beavers.Read)
            {
                if (singleId.HasValue && c.Id != singleId.Value) continue;
                if (hasFilter && !PassesFilter(c.Name, c.X, c.Y, filterName, filterX, filterY, filterRadius)) continue;
                if (offset > 0 && skipped < offset) { skipped++; continue; }
                if (paginated && emitted >= limit) break;
                emitted++;
                jw.OpenObj()
                    .Prop("id", c.Id)
                    .Prop("name", c.Name)
                    .Prop("x", c.X).Prop("y", c.Y).Prop("z", c.Z)
                    .Prop("wellbeing", c.Wellbeing, "F1")
                    .Prop("isBot", c.IsBot);
                if (!fullDetail)
                {
                    float wb = c.Wellbeing;
                    string tier = wb >= 16 ? "ecstatic" : wb >= 12 ? "happy" : wb >= 8 ? "okay" : wb >= 4 ? "unhappy" : "miserable";
                    jw.Prop("tier", tier).Prop("workplace", c.Workplace ?? "");
                    string criticalStr = "", unmet = "";
                    if (c.Needs != null)
                    {
                        foreach (var n in c.Needs)
                        {
                            if (n.Critical != 0) criticalStr = criticalStr.Length > 0 ? criticalStr + "+" + n.Id : n.Id;
                            else if (n.Favorable == 0 && n.Active != 0) unmet = unmet.Length > 0 ? unmet + "+" + n.Id : n.Id;
                        }
                    }
                    jw.Prop("critical", criticalStr).Prop("unmet", unmet).CloseObj();
                    continue;
                }
                jw.Prop("anyCritical", c.AnyCritical)
                    .Prop("workplace", c.Workplace ?? "")
                    .Prop("district", c.District ?? "")
                    .Prop("hasHome", c.HasHome)
                    .Prop("contaminated", c.Contaminated)
                    .Prop("lifeProgress", c.Life != null ? c.LifeProgress : 0f)
                    .Prop("deterioration", c.Deteriorable != null ? c.DeteriorationProgress : 0f, "F3")
                    .Prop("liftingCapacity", c.Carrier != null ? c.LiftingCapacity : 0)
                    .Prop("overburdened", c.Overburdened)
                    .Prop("carrying", c.IsCarrying != 0 ? c.CarryingGood : "")
                    .Prop("carryAmount", c.IsCarrying != 0 ? c.CarryAmount : 0);
                jw.Arr("needs");
                if (c.Needs != null)
                {
                    foreach (var n in c.Needs)
                    {
                        jw.OpenObj()
                            .Prop("id", n.Id)
                            .Prop("points", n.Points)
                            .Prop("wellbeing", n.Wellbeing)
                            .Prop("favorable", n.Favorable)
                            .Prop("critical", n.Critical)
                            .Prop("group", n.Group)
                            .CloseObj();
                    }
                }
                jw.CloseArr().CloseObj();
            }
            jw.CloseArr();
            if (paginated) jw.CloseObj();
            return jw.ToString();
        }
        public object CollectDistribution(string format = "toon") => _distributionRoute.Collect(format);
        public object CollectScience(string format = "toon") => _scienceRoute.Collect(format);
        public object CollectWellbeing(string format = "toon")
        {
            try
            {
                var beaverNeeds = _factionNeedService.GetBeaverNeeds();
                var groupNeeds = new Dictionary<string, List<NeedSpec>>();
                foreach (var ns in beaverNeeds)
                {
                    var groupId = ns.NeedGroupId;
                    if (string.IsNullOrEmpty(groupId)) continue;
                    if (!groupNeeds.ContainsKey(groupId))
                        groupNeeds[groupId] = new List<NeedSpec>();
                    groupNeeds[groupId].Add(ns);
                }
                int beaverCount = 0;
                var groupTotals = new Dictionary<string, float>();
                var groupMaxTotals = new Dictionary<string, float>();
                var needToGroup = new Dictionary<string, string>();
                foreach (var kvp in groupNeeds)
                    foreach (var ns in kvp.Value)
                        needToGroup[ns.Id] = kvp.Key;
                foreach (var c in _cache.Beavers.Read)
                {
                    if (c.Needs == null) continue;
                    beaverCount++;
                    foreach (var n in c.Needs)
                    {
                        if (!needToGroup.TryGetValue(n.Id, out var groupId)) continue;
                        if (!groupTotals.ContainsKey(groupId)) { groupTotals[groupId] = 0f; groupMaxTotals[groupId] = 0f; }
                        groupTotals[groupId] += n.Wellbeing;
                    }
                    foreach (var kvp in groupNeeds)
                    {
                        var groupId = kvp.Key;
                        float groupMax = 0f;
                        foreach (var ns in kvp.Value) groupMax += ns.FavorableWellbeing;
                        if (!groupMaxTotals.ContainsKey(groupId)) groupMaxTotals[groupId] = 0f;
                        groupMaxTotals[groupId] += groupMax;
                    }
                }
                var jw = _cache.Jw.BeginObj().Prop("beavers", beaverCount).Arr("categories");
                foreach (var kvp in groupNeeds)
                {
                    var groupId = kvp.Key;
                    float avgCurrent = beaverCount > 0 ? groupTotals.GetValueOrDefault(groupId) / beaverCount : 0;
                    float avgMax = beaverCount > 0 ? groupMaxTotals.GetValueOrDefault(groupId) / beaverCount : 0;
                    jw.OpenObj().Prop("group", groupId).Prop("current", (float)Math.Round(avgCurrent, 1), "F1").Prop("max", (float)Math.Round(avgMax, 1), "F1");
                    jw.Arr("needs");
                    foreach (var ns in kvp.Value)
                        jw.OpenObj().Prop("id", ns.Id).Prop("favorableWellbeing", ns.FavorableWellbeing, "F1").Prop("unfavorableWellbeing", ns.UnfavorableWellbeing, "F1").CloseObj();
                    jw.CloseArr().CloseObj();
                }
                jw.CloseArr().CloseObj();
                return jw.ToString();
            }
            catch (Exception ex) { TimberbotLog.Error("wellbeing", ex); return _cache.Jw.Error("operation_failed: " + ex.Message); }
        }
        public object CollectNotifications(string format = "toon", int limit = 100, int offset = 0)
            => _notificationsRoute.Collect(format, limit, offset);
        public object CollectWorkHours() => _workHoursRoute.Collect();
        public object CollectPowerNetworks(string format = "toon") => _powerRoute.Collect(format);
        public object CollectSpeed() => _speedRoute.Collect();
        public string GetSettlementName()
        {
            try
            {
                return _settlementStore.RequestFresh(2000)?.Name ?? "unknown";
            }
            catch (TimeoutException)
            {
                return "unknown";
            }
        }

        private SettlementSnapshot BuildSettlementSnapshot()
        {
            return new SettlementSnapshot { Name = ResolveSettlementName() };
        }

        private TimeSnapshot BuildTimeSnapshot()
        {
            return new TimeSnapshot
            {
                DayNumber = _dayNightCycle.DayNumber,
                DayProgress = _dayNightCycle.DayProgress,
                PartialDayNumber = _dayNightCycle.PartialDayNumber
            };
        }

        private WeatherSnapshot BuildWeatherSnapshot()
        {
            return new WeatherSnapshot
            {
                Cycle = (int)_gameCycleService.Cycle,
                CycleDay = _gameCycleService.CycleDay,
                IsHazardous = _weatherService.IsHazardousWeather,
                TemperateWeatherDuration = _weatherService.TemperateWeatherDuration,
                HazardousWeatherDuration = _weatherService.HazardousWeatherDuration,
                CycleLengthInDays = _weatherService.CycleLengthInDays
            };
        }

        private SpeedSnapshot BuildSpeedSnapshot()
        {
            var raw = _speedManager.CurrentSpeed;
            int level = Array.IndexOf(TimberbotRead.SpeedScale, raw);
            if (level < 0) level = 0;
            return new SpeedSnapshot { Speed = level };
        }

        private WorkHoursSnapshot BuildWorkHoursSnapshot()
        {
            return new WorkHoursSnapshot
            {
                EndHours = _workingHoursManager.EndHours,
                AreWorkingHours = _workingHoursManager.AreWorkingHours
            };
        }

        private RawJsonSnapshot BuildScienceSnapshot()
        {
            var jw = _scienceBuildJw.Reset().OpenObj().Prop("points", _scienceService.SciencePoints);
            jw.Arr("unlockables");
            foreach (var building in _buildingService.Buildings)
            {
                var bs = building.GetSpec<BuildingSpec>();
                if (bs == null || bs.ScienceCost <= 0) continue;
                var templateSpec = building.GetSpec<Timberborn.TemplateSystem.TemplateSpec>();
                var name = templateSpec?.TemplateName ?? "unknown";
                jw.OpenObj()
                    .Prop("name", name)
                    .Prop("cost", bs.ScienceCost)
                    .Prop("unlocked", _buildingUnlockingService.Unlocked(bs))
                    .CloseObj();
            }
            jw.CloseArr().CloseObj();
            return new RawJsonSnapshot { Json = jw.ToString() };
        }

        private RawJsonSnapshot BuildDistributionSnapshot()
        {
            var jw = _distributionBuildJw.BeginArr();
            foreach (var dc in _districtCenterRegistry.FinishedDistrictCenters)
            {
                var distSetting = dc.GetComponent<Timberborn.DistributionSystem.DistrictDistributionSetting>();
                if (distSetting == null) continue;
                jw.OpenObj().Prop("district", dc.DistrictName).Arr("goods");
                foreach (var gs in distSetting.GoodDistributionSettings)
                {
                    jw.OpenObj()
                        .Prop("good", gs.GoodId)
                        .Prop("importOption", gs.ImportOption.ToString())
                        .Prop("exportThreshold", gs.ExportThreshold, "F0")
                        .CloseObj();
                }
                jw.CloseArr().CloseObj();
            }
            return new RawJsonSnapshot { Json = jw.End() };
        }

        private NotificationItem[] BuildNotificationsSnapshot()
        {
            try
            {
                var items = new List<NotificationItem>();
                foreach (var n in _notificationSaver.Notifications)
                {
                    items.Add(new NotificationItem
                    {
                        Subject = n.Subject.ToString(),
                        Description = n.Description.ToString(),
                        Cycle = n.Cycle,
                        CycleDay = n.CycleDay
                    });
                }
                return items.ToArray();
            }
            catch (Exception ex)
            {
                TimberbotLog.Error("readv2.notifications", ex);
                return Array.Empty<NotificationItem>();
            }
        }

        private AlertItem[] BuildAlertsFromBuildings()
        {
            var snapshot = _snapshot.RequestFresh(false, 2000);
            var alerts = new List<AlertItem>();
            for (int i = 0; i < snapshot.Count; i++)
            {
                var d = snapshot.Definitions[i];
                var s = snapshot.States[i];
                if (s.DesiredWorkers > 0 && s.AssignedWorkers < s.DesiredWorkers)
                {
                    alerts.Add(new AlertItem
                    {
                        Type = "unstaffed",
                        Id = d.Id,
                        Name = d.Name,
                        Workers = $"{s.AssignedWorkers}/{s.DesiredWorkers}"
                    });
                }
                if (d.IsConsumer != 0 && s.Powered == 0)
                    alerts.Add(new AlertItem { Type = "unpowered", Id = d.Id, Name = d.Name });
                if (s.Unreachable != 0)
                    alerts.Add(new AlertItem { Type = "unreachable", Id = d.Id, Name = d.Name });
            }
            return alerts.ToArray();
        }

        private PowerNetworkItem[] BuildPowerFromBuildings()
        {
            var snapshot = _snapshot.RequestFresh(false, 2000);
            var networks = new Dictionary<int, PowerNetworkBuilder>();
            for (int i = 0; i < snapshot.Count; i++)
            {
                var d = snapshot.Definitions[i];
                var s = snapshot.States[i];
                if (s.PowerNetworkId == 0) continue;
                if (!networks.TryGetValue(s.PowerNetworkId, out var builder))
                {
                    builder = new PowerNetworkBuilder
                    {
                        Id = s.PowerNetworkId,
                        Supply = s.PowerSupply,
                        Demand = s.PowerDemand,
                        Buildings = new List<PowerBuildingItem>()
                    };
                    networks[s.PowerNetworkId] = builder;
                }
                builder.Buildings.Add(new PowerBuildingItem
                {
                    Name = d.Name,
                    Id = d.Id,
                    IsGenerator = d.IsGenerator,
                    NominalOutput = d.NominalPowerOutput,
                    NominalInput = d.NominalPowerInput
                });
            }

            var result = new PowerNetworkItem[networks.Count];
            int index = 0;
            foreach (var builder in networks.Values)
            {
                result[index++] = new PowerNetworkItem
                {
                    Id = builder.Id,
                    Supply = builder.Supply,
                    Demand = builder.Demand,
                    Buildings = builder.Buildings.ToArray()
                };
            }
            return result;
        }

        private string ResolveSettlementName()
        {
            try
            {
                var obj = (object)_gameCycleService;
                foreach (var field in new[] { "_singletonLoader", "_serializedWorldSupplier", "_sceneLoader", "_sceneParameters" })
                {
                    var fi = obj.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (fi == null) return "unknown";
                    obj = fi.GetValue(obj);
                    if (obj == null) return "unknown";
                }
                var saveRef = obj.GetType().GetProperty("SaveReference")?.GetValue(obj);
                if (saveRef == null) return "unknown";
                var settRef = saveRef.GetType().GetProperty("SettlementReference")?.GetValue(saveRef);
                if (settRef == null) return "unknown";
                var name = settRef.GetType().GetProperty("SettlementName")?.GetValue(settRef);
                return name?.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static void RefreshState(BuildingState s, TrackedBuildingRef t)
        {
            var bo = t.BlockObject;
            s.Finished = bo != null && bo.IsFinished ? 1 : 0;
            s.Paused = t.Pausable != null && t.Pausable.Paused ? 1 : 0;
            s.Unreachable = t.Reachability != null && t.Reachability.IsAnyUnreachable() ? 1 : 0;
            s.Reachable = t.Reachability != null ? (s.Unreachable == 0 ? 1 : 0) : 0;
            s.Powered = t.Mechanical != null && t.Mechanical.ActiveAndPowered ? 1 : 0;
            s.District = t.DistrictBuilding != null ? t.DistrictBuilding.District?.DistrictName : null;
            if (t.Workplace != null)
            {
                s.AssignedWorkers = t.Workplace.NumberOfAssignedWorkers;
                s.DesiredWorkers = t.Workplace.DesiredWorkers;
                s.MaxWorkers = t.Workplace.MaxWorkers;
            }
            else { s.AssignedWorkers = 0; s.DesiredWorkers = 0; s.MaxWorkers = 0; }
            if (t.Dwelling != null)
            {
                s.Dwellers = t.Dwelling.NumberOfDwellers;
                s.MaxDwellers = t.Dwelling.MaxBeavers;
            }
            else { s.Dwellers = 0; s.MaxDwellers = 0; }
            s.FloodgateHeight = t.Floodgate != null ? t.Floodgate.Height : 0f;
            s.ConstructionPriority = t.BuilderPrio != null ? TimberbotEntityCache.GetPriorityName(t.BuilderPrio.Priority) : null;
            s.WorkplacePriorityStr = t.WorkplacePrio != null ? TimberbotEntityCache.GetPriorityName(t.WorkplacePrio.Priority) : null;
            if (t.Site != null)
            {
                s.BuildProgress = t.Site.BuildTimeProgress;
                s.MaterialProgress = t.Site.MaterialProgress;
                s.HasMaterials = t.Site.HasMaterialsToResumeBuilding ? 1 : 0;
            }
            else { s.BuildProgress = 0f; s.MaterialProgress = 0f; s.HasMaterials = 0; }
            s.ClutchEngaged = t.Clutch != null && t.Clutch.IsEngaged ? 1 : 0;
            s.WonderActive = t.Wonder != null && t.Wonder.IsActive ? 1 : 0;
            s.PowerDemand = 0; s.PowerSupply = 0; s.PowerNetworkId = 0;
            if (t.PowerNode != null)
            {
                try
                {
                    var g = t.PowerNode.Graph;
                    if (g != null)
                    {
                        s.PowerDemand = (int)g.PowerDemand;
                        s.PowerSupply = (int)g.PowerSupply;
                        s.PowerNetworkId = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(g);
                    }
                }
                catch (Exception ex) { TimberbotLog.Error("readv2.power", ex); }
            }
            if (t.Manufactory != null)
            {
                s.CurrentRecipe = t.Manufactory.HasCurrentRecipe ? t.Manufactory.CurrentRecipe.Id : "";
                s.ProductionProgress = t.Manufactory.ProductionProgress;
                s.ReadyToProduce = t.Manufactory.IsReadyToProduce ? 1 : 0;
            }
            else { s.CurrentRecipe = ""; s.ProductionProgress = 0f; s.ReadyToProduce = 0; }
            s.NeedsNutrients = t.BreedingPod != null && t.BreedingPod.NeedsNutrients ? 1 : 0;
            s.Stock = 0; s.Capacity = 0;
            if (t.Inventories != null)
            {
                try
                {
                    var allInv = t.Inventories.AllInventories;
                    for (int ii = 0; ii < allInv.Count; ii++)
                    {
                        var inv = allInv[ii];
                        if (inv.ComponentName == ConstructionSiteInventoryInitializer.InventoryComponentName) continue;
                        s.Stock += inv.TotalAmountInStock;
                        s.Capacity += inv.Capacity;
                    }
                }
                catch (Exception ex) { TimberbotLog.Error("readv2.stock", ex); }
            }
        }

        private void RefreshDetail(BuildingDetailState d, TrackedBuildingRef t)
        {
            d.Inventory.Clear();
            d.InventoryToon = "";
            if (t.Inventories != null)
            {
                try
                {
                    var allInv = t.Inventories.AllInventories;
                    for (int ii = 0; ii < allInv.Count; ii++)
                    {
                        var item = allInv[ii];
                        if (item.ComponentName == ConstructionSiteInventoryInitializer.InventoryComponentName) continue;
                        var stock = item.Stock;
                        for (int si = 0; si < stock.Count; si++)
                        {
                            var ga = stock[si];
                            if (ga.Amount <= 0) continue;
                            if (d.Inventory.ContainsKey(ga.GoodId)) d.Inventory[ga.GoodId] += ga.Amount;
                            else d.Inventory[ga.GoodId] = ga.Amount;
                        }
                    }
                }
                catch (Exception ex) { TimberbotLog.Error("readv2.inventory", ex); }
                d.InventoryToon = ToToonDict(d.Inventory);
            }
            d.Recipes.Clear();
            d.RecipesToon = "";
            if (t.Manufactory != null)
            {
                foreach (var r in t.Manufactory.ProductionRecipes)
                    d.Recipes.Add(r.Id);
                _toonSb.Clear();
                for (int i = 0; i < d.Recipes.Count; i++)
                {
                    if (i > 0) _toonSb.Append('/');
                    _toonSb.Append(d.Recipes[i]);
                }
                d.RecipesToon = _toonSb.ToString();
            }
            d.NutrientStock.Clear();
            if (t.BreedingPod != null)
            {
                try
                {
                    foreach (var ga in t.BreedingPod.Nutrients)
                        if (ga.Amount > 0) d.NutrientStock[ga.GoodId] = ga.Amount;
                }
                catch (Exception ex) { TimberbotLog.Error("readv2.nutrients", ex); }
            }
        }

        private string ToToonDict(Dictionary<string, int> dict)
        {
            if (dict.Count == 0) return "";
            _toonSb.Clear();
            foreach (var kvp in dict)
            {
                if (_toonSb.Length > 0) _toonSb.Append('/');
                _toonSb.Append(kvp.Key).Append(':').Append(kvp.Value);
            }
            return _toonSb.ToString();
        }

        private static bool PassesFilter(string entityName, int entityX, int entityY,
            string filterName, int filterX, int filterY, int filterRadius)
        {
            if (filterName != null && entityName.IndexOf(filterName, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (filterRadius > 0 && (Math.Abs(entityX - filterX) + Math.Abs(entityY - filterY)) > filterRadius)
                return false;
            return true;
        }

        private object CollectNaturalResourcesJw(TimberbotJw jw, HashSet<string> species, int limit = 100, int offset = 0,
            string filterName = null, int filterX = 0, int filterY = 0, int filterRadius = 0)
        {
            bool paginated = limit > 0;
            bool hasFilter = filterName != null || filterRadius > 0;
            int skipped = 0, emitted = 0;
            int total = 0;
            if (paginated)
                foreach (var c in _cache.NaturalResources.Read)
                    if (c.Cuttable != null && species.Contains(c.Name) && (!hasFilter || PassesFilter(c.Name, c.X, c.Y, filterName, filterX, filterY, filterRadius))) total++;

            jw.Reset();
            if (paginated) jw.OpenObj().Prop("total", total).Prop("offset", offset).Prop("limit", limit).Key("items");
            jw.OpenArr();
            foreach (var c in _cache.NaturalResources.Read)
            {
                if (c.Cuttable == null) continue;
                if (!species.Contains(c.Name)) continue;
                if (hasFilter && !PassesFilter(c.Name, c.X, c.Y, filterName, filterX, filterY, filterRadius)) continue;
                if (offset > 0 && skipped < offset) { skipped++; continue; }
                if (paginated && emitted >= limit) break;
                emitted++;
                jw.OpenObj()
                    .Prop("id", c.Id)
                    .Prop("name", c.Name)
                    .Prop("x", c.X).Prop("y", c.Y).Prop("z", c.Z)
                    .Prop("marked", c.Marked)
                    .Prop("alive", c.Alive)
                    .Prop("grown", c.Grown)
                    .Prop("growth", c.Growth)
                    .CloseObj();
            }
            jw.CloseArr();
            if (paginated) jw.CloseObj();
            return jw.ToString();
        }

        private void WriteClustersFiltered(TimberbotJw jw, string key,
            List<TimberbotEntityCache.CachedNaturalResource> resources,
            HashSet<string> excludeSpecies,
            int dcX, int dcY, int dcZ, int maxDist, int cellSize, int top)
        {
            _clusterCells.Clear(); _clusterSpecies.Clear();
            foreach (var nr in resources)
            {
                if (excludeSpecies == null) { if (nr.Cuttable == null) continue; }
                else { if (nr.Gatherable == null || excludeSpecies.Contains(nr.Name)) continue; }
                if (nr.Alive == 0) continue;
                int cx = nr.X / cellSize * cellSize + cellSize / 2;
                int cy = nr.Y / cellSize * cellSize + cellSize / 2;
                if (nr.Z != dcZ || Math.Abs(cx - dcX) + Math.Abs(cy - dcY) > maxDist) continue;
                long k = (long)cx * 100000 + cy;
                if (!_clusterCells.ContainsKey(k))
                { _clusterCells[k] = new int[] { 0, 0, cx, cy, nr.Z }; _clusterSpecies[k] = new Dictionary<string, int>(); }
                _clusterCells[k][1]++;
                _clusterSpecies[k][nr.Name] = _clusterSpecies[k].GetValueOrDefault(nr.Name) + 1;
                if (nr.Grown != 0) _clusterCells[k][0]++;
            }
            _clusterSorted.Clear(); _clusterSorted.AddRange(_clusterCells.Keys);
            _clusterSorted.Sort((a, b) => _clusterCells[b][0].CompareTo(_clusterCells[a][0]));
            jw.Arr(key);
            for (int i = 0; i < Math.Min(top, _clusterSorted.Count); i++)
            {
                var s = _clusterCells[_clusterSorted[i]];
                jw.OpenObj().Prop("x", s[2]).Prop("y", s[3]).Prop("z", s[4]).Prop("grown", s[0]).Prop("total", s[1]);
                var sp = _clusterSpecies[_clusterSorted[i]];
                jw.Obj("species");
                foreach (var kv in sp) jw.Prop(kv.Key, kv.Value);
                jw.CloseObj().CloseObj();
            }
            jw.CloseArr();
        }

        private void TryAddTracked(EntityComponent ec)
        {
            if (ec.GetComponent<Building>() == null) return;
            int id = ec.GameObject.GetInstanceID();
            if (_trackedById.ContainsKey(id)) return;

            var t = new TrackedBuildingRef
            {
                Id = id,
                Name = TimberbotEntityCache.CleanName(ec.GameObject.name),
                BlockObject = ec.GetComponent<BlockObject>(),
                Pausable = ec.GetComponent<PausableBuilding>(),
                Floodgate = ec.GetComponent<Floodgate>(),
                BuilderPrio = ec.GetComponent<BuilderPrioritizable>(),
                Workplace = ec.GetComponent<Workplace>(),
                WorkplacePrio = ec.GetComponent<WorkplacePriority>(),
                Reachability = ec.GetComponent<EntityReachabilityStatus>(),
                Mechanical = ec.GetComponent<MechanicalBuilding>(),
                Status = ec.GetComponent<StatusSubject>(),
                PowerNode = ec.GetComponent<MechanicalNode>(),
                Site = ec.GetComponent<ConstructionSite>(),
                Inventories = ec.GetComponent<Inventories>(),
                Wonder = ec.GetComponent<Wonder>(),
                Dwelling = ec.GetComponent<Dwelling>(),
                Clutch = ec.GetComponent<Clutch>(),
                Manufactory = ec.GetComponent<Manufactory>(),
                BreedingPod = ec.GetComponent<BreedingPod>(),
                RangedEffect = ec.GetComponent<RangedEffectBuildingSpec>(),
                DistrictBuilding = ec.GetComponent<DistrictBuilding>(),
            };

            var def = new BuildingDefinition
            {
                Id = id,
                Name = t.Name,
                HasFloodgate = t.Floodgate != null ? 1 : 0,
                FloodgateMaxHeight = t.Floodgate?.MaxHeight ?? 0f,
                HasClutch = t.Clutch != null ? 1 : 0,
                HasWonder = t.Wonder != null ? 1 : 0,
                IsGenerator = t.PowerNode?.IsGenerator == true ? 1 : 0,
                IsConsumer = t.PowerNode?.IsConsumer == true ? 1 : 0,
                NominalPowerInput = t.PowerNode?._nominalPowerInput ?? 0,
                NominalPowerOutput = t.PowerNode?._nominalPowerOutput ?? 0,
                EffectRadius = t.RangedEffect?.EffectRadius ?? 0
            };

            var bo = t.BlockObject;
            if (bo != null)
            {
                var coords = bo.Coordinates;
                def.X = coords.x;
                def.Y = coords.y;
                def.Z = coords.z;
                def.Orientation = TimberbotEntityCache.OrientNames[(int)bo.Orientation];
                var occupied = new List<(int, int, int)>();
                try
                {
                    foreach (var block in bo.PositionedBlocks.GetAllBlocks())
                    {
                        var tc = block.Coordinates;
                        occupied.Add((tc.x, tc.y, tc.z));
                    }
                }
                catch { occupied.Add((def.X, def.Y, def.Z)); }
                def.OccupiedTiles = occupied.ToArray();
                if (bo.HasEntrance)
                {
                    try
                    {
                        var ent = bo.PositionedEntrance.DoorstepCoordinates;
                        def.HasEntrance = 1;
                        def.EntranceX = ent.x;
                        def.EntranceY = ent.y;
                    }
                    catch (Exception ex) { TimberbotLog.Error("readv2.entrance", ex); }
                }
            }

            t.Definition = def;
            _tracked.Add(t);
            _trackedById[id] = t;
            _snapshot.MarkDirty();
        }

        private void RemoveTracked(EntityComponent ec)
        {
            int id = ec.GameObject.GetInstanceID();
            if (!_trackedById.TryGetValue(id, out var tracked)) return;
            _trackedById.Remove(id);
            _tracked.Remove(tracked);
            _snapshot.MarkDirty();
        }

        [OnEvent]
        public void OnEntityInitialized(EntityInitializedEvent e)
        {
            TryAddTracked(e.Entity);
        }

        [OnEvent]
        public void OnEntityDeleted(EntityDeletedEvent e)
        {
            RemoveTracked(e.Entity);
        }

        private sealed class TrackedBuildingRef
        {
            public int Id;
            public string Name;
            public BlockObject BlockObject;
            public PausableBuilding Pausable;
            public Floodgate Floodgate;
            public BuilderPrioritizable BuilderPrio;
            public Workplace Workplace;
            public WorkplacePriority WorkplacePrio;
            public EntityReachabilityStatus Reachability;
            public MechanicalBuilding Mechanical;
            public StatusSubject Status;
            public MechanicalNode PowerNode;
            public ConstructionSite Site;
            public Inventories Inventories;
            public Wonder Wonder;
            public Dwelling Dwelling;
            public Clutch Clutch;
            public Manufactory Manufactory;
            public BreedingPod BreedingPod;
            public RangedEffectBuildingSpec RangedEffect;
            public DistrictBuilding DistrictBuilding;
            public BuildingDefinition Definition;
        }

        private sealed class BuildingDefinition
        {
            public int Id;
            public string Name;
            public int X, Y, Z;
            public string Orientation;
            public int HasFloodgate;
            public float FloodgateMaxHeight;
            public int HasClutch;
            public int HasWonder;
            public int IsGenerator, IsConsumer;
            public int NominalPowerInput, NominalPowerOutput;
            public int EffectRadius;
            public (int x, int y, int z)[] OccupiedTiles;
            public int HasEntrance;
            public int EntranceX, EntranceY;
        }

        private sealed class BuildingState
        {
            public int Finished, Paused, Unreachable, Reachable, Powered;
            public string District;
            public int AssignedWorkers, DesiredWorkers, MaxWorkers;
            public int Dwellers, MaxDwellers;
            public float FloodgateHeight;
            public string ConstructionPriority, WorkplacePriorityStr;
            public float BuildProgress, MaterialProgress;
            public int HasMaterials;
            public int ClutchEngaged, WonderActive;
            public int PowerDemand, PowerSupply, PowerNetworkId;
            public string CurrentRecipe;
            public float ProductionProgress;
            public int ReadyToProduce;
            public int NeedsNutrients;
            public int Stock, Capacity;
        }

        private sealed class BuildingDetailState
        {
            public readonly Dictionary<string, int> Inventory = new Dictionary<string, int>();
            public string InventoryToon = "";
            public readonly List<string> Recipes = new List<string>();
            public string RecipesToon = "";
            public readonly Dictionary<string, int> NutrientStock = new Dictionary<string, int>();
        }

        private interface ICollectionSchema<TDef, TState, TDetail>
        {
            int GetId(TDef def);
            string GetName(TDef def);
            int GetX(TDef def, TState state);
            int GetY(TDef def, TState state);
            bool IncludeRow(TDef def, TState state);
            void WriteRow(TimberbotJw jw, string format, bool fullDetail, TDef def, TState state, TDetail detail);
        }

        private sealed class CollectionQuery
        {
            public string Format;
            public int? SingleId;
            public int Limit;
            public int Offset;
            public string FilterName;
            public int FilterX;
            public int FilterY;
            public int FilterRadius;
            public bool HasFilter;
            public bool Paginated;
            public bool NeedsFullDetail;

            public static CollectionQuery Parse(string format, string detail, int limit, int offset, string filterName, int filterX, int filterY, int filterRadius)
            {
                int? singleId = null;
                if (!string.IsNullOrEmpty(detail) && detail.StartsWith("id:", StringComparison.Ordinal))
                {
                    if (int.TryParse(detail.Substring(3), out int parsed))
                        singleId = parsed;
                }
                return new CollectionQuery
                {
                    Format = format ?? "toon",
                    SingleId = singleId,
                    Limit = limit,
                    Offset = offset,
                    FilterName = filterName,
                    FilterX = filterX,
                    FilterY = filterY,
                    FilterRadius = filterRadius,
                    HasFilter = filterName != null || filterRadius > 0,
                    Paginated = limit > 0 && !singleId.HasValue,
                    NeedsFullDetail = detail == "full" || singleId.HasValue
                };
            }
        }

        private sealed class ProjectionSnapshot<TDef, TState, TDetail>
            where TDef : class
            where TState : class, new()
            where TDetail : class, new()
        {
            public delegate TDef GetDefinition(int index);
            public delegate void RefreshState(TState state, int index);
            public delegate void RefreshDetail(TDetail detail, int index);

            private readonly object _lock = new object();
            private bool _refreshRequested;
            private bool _fullRequested;
            private readonly List<Waiter> _waiters = new List<Waiter>();
            private readonly List<Waiter> _wakeBatch = new List<Waiter>();
            private readonly Buffer _bufA = new Buffer();
            private readonly Buffer _bufB = new Buffer();
            private Buffer _writeBuf;
            private Snapshot _published = Snapshot.Empty;
            private bool _structureDirty = true;
            private int _sequence;

            public ProjectionSnapshot()
            {
                _writeBuf = _bufA;
            }

            public int Sequence => _sequence;
            public int Count => _published.Count;
            public float PublishedAt => _published.PublishedAt;
            public void MarkDirty() => _structureDirty = true;

            public void ProcessPending(float now, int count, GetDefinition getDef, RefreshState refreshState, RefreshDetail refreshDetail)
            {
                bool full;
                lock (_lock)
                {
                    if (!_refreshRequested) return;
                    _refreshRequested = false;
                    full = _fullRequested;
                    _fullRequested = false;
                    _wakeBatch.Clear();
                    _wakeBatch.AddRange(_waiters);
                    _waiters.Clear();
                }

                try
                {
                    Publish(count, getDef, refreshState, full ? refreshDetail : null, now);
                }
                finally
                {
                    for (int i = 0; i < _wakeBatch.Count; i++)
                        _wakeBatch[i].Signal.Set();
                }
            }

            public Snapshot RequestFresh(bool fullDetail, int timeoutMs)
            {
                var waiter = new Waiter();
                lock (_lock)
                {
                    _refreshRequested = true;
                    if (fullDetail) _fullRequested = true;
                    _waiters.Add(waiter);
                }

                if (!waiter.Signal.Wait(timeoutMs))
                {
                    lock (_lock) _waiters.Remove(waiter);
                    throw new TimeoutException();
                }
                return _published;
            }

            public void PublishNow(float now, int count, GetDefinition getDef, RefreshState refreshState)
            {
                Publish(count, getDef, refreshState, null, now);
            }

            private void Publish(int count, GetDefinition getDef, RefreshState refreshState, RefreshDetail refreshDetail, float now)
            {
                var buf = _writeBuf;
                if (_structureDirty)
                {
                    _structureDirty = false;
                    buf.EnsureCapacity(count);
                    for (int i = 0; i < count; i++)
                        buf.Definitions[i] = getDef(i);
                }

                for (int i = 0; i < count; i++)
                {
                    refreshState(buf.States[i], i);
                    refreshDetail?.Invoke(buf.Details[i], i);
                }

                _published = new Snapshot
                {
                    Definitions = buf.Definitions,
                    States = buf.States,
                    Details = refreshDetail != null ? buf.Details : null,
                    Count = count,
                    PublishedAt = now
                };
                _sequence++;
                _writeBuf = ReferenceEquals(buf, _bufA) ? _bufB : _bufA;
                if (_writeBuf.Length < count)
                    _structureDirty = true;
            }

            public sealed class Snapshot
            {
                public static readonly Snapshot Empty = new Snapshot
                {
                    Definitions = Array.Empty<TDef>(),
                    States = Array.Empty<TState>(),
                    Details = null,
                    Count = 0,
                    PublishedAt = 0f
                };

                public TDef[] Definitions;
                public TState[] States;
                public TDetail[] Details;
                public int Count;
                public float PublishedAt;
            }

            private sealed class Waiter
            {
                public readonly ManualResetEventSlim Signal = new ManualResetEventSlim(false);
            }

            private sealed class Buffer
            {
                public TDef[] Definitions = Array.Empty<TDef>();
                public TState[] States = Array.Empty<TState>();
                public TDetail[] Details = Array.Empty<TDetail>();
                public int Length;

                public void EnsureCapacity(int count)
                {
                    if (count <= Length) { Length = count; return; }
                    int capacity = Math.Max(count, Length * 2);
                    var newDefs = new TDef[capacity];
                    var newStates = new TState[capacity];
                    var newDetails = new TDetail[capacity];
                    int copyCount = Math.Min(Length, count);
                    Array.Copy(Definitions, newDefs, copyCount);
                    Array.Copy(States, newStates, copyCount);
                    Array.Copy(Details, newDetails, copyCount);
                    for (int i = copyCount; i < capacity; i++)
                    {
                        newStates[i] = new TState();
                        newDetails[i] = new TDetail();
                    }
                    Definitions = newDefs;
                    States = newStates;
                    Details = newDetails;
                    Length = count;
                }
            }
        }

        private sealed class CollectionRoute<TDef, TState, TDetail>
            where TDef : class
            where TState : class, new()
            where TDetail : class, new()
        {
            private readonly TimberbotJw _jw;
            private readonly Func<bool, int, ProjectionSnapshot<TDef, TState, TDetail>.Snapshot> _snapshotProvider;
            private readonly ICollectionSchema<TDef, TState, TDetail> _schema;

            public CollectionRoute(
                TimberbotJw jw,
                Func<bool, int, ProjectionSnapshot<TDef, TState, TDetail>.Snapshot> snapshotProvider,
                ICollectionSchema<TDef, TState, TDetail> schema)
            {
                _jw = jw;
                _snapshotProvider = snapshotProvider;
                _schema = schema;
            }

            public object Collect(string format, string detail, int limit, int offset, string filterName, int filterX, int filterY, int filterRadius)
            {
                var query = CollectionQuery.Parse(format, detail, limit, offset, filterName, filterX, filterY, filterRadius);
                ProjectionSnapshot<TDef, TState, TDetail>.Snapshot snapshot;
                try { snapshot = _snapshotProvider(query.NeedsFullDetail, 2000); }
                catch (TimeoutException) { return _jw.Error("refresh_timeout"); }

                int total = snapshot.Count;
                if (query.Paginated && query.HasFilter)
                {
                    total = 0;
                    for (int i = 0; i < snapshot.Count; i++)
                        if (ShouldInclude(query, snapshot.Definitions[i], snapshot.States[i])) total++;
                }

                int skipped = 0, emitted = 0;
                var jw = _jw.Reset();
                if (query.Paginated)
                    jw.OpenObj().Prop("total", total).Prop("offset", query.Offset).Prop("limit", query.Limit).Key("items");
                jw.OpenArr();
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var def = snapshot.Definitions[i];
                    var state = snapshot.States[i];
                    if (!ShouldInclude(query, def, state)) continue;
                    if (query.Offset > 0 && skipped < query.Offset) { skipped++; continue; }
                    if (query.Paginated && emitted >= query.Limit) break;
                    emitted++;
                    var detailState = query.NeedsFullDetail && snapshot.Details != null ? snapshot.Details[i] : null;
                    _schema.WriteRow(jw, query.Format, query.NeedsFullDetail, def, state, detailState);
                }
                jw.CloseArr();
                if (query.Paginated) jw.CloseObj();
                return jw.ToString();
            }

            private bool ShouldInclude(CollectionQuery query, TDef def, TState state)
            {
                if (!_schema.IncludeRow(def, state)) return false;
                if (query.SingleId.HasValue && _schema.GetId(def) != query.SingleId.Value) return false;
                var name = _schema.GetName(def) ?? "";
                var x = _schema.GetX(def, state);
                var y = _schema.GetY(def, state);
                if (query.FilterName != null && name.IndexOf(query.FilterName, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
                if (query.FilterRadius > 0 && (Math.Abs(x - query.FilterX) + Math.Abs(y - query.FilterY)) > query.FilterRadius)
                    return false;
                return true;
            }
        }

        private sealed class BuildingCollectionSchema : ICollectionSchema<BuildingDefinition, BuildingState, BuildingDetailState>
        {
            public int GetId(BuildingDefinition def) => def.Id;
            public string GetName(BuildingDefinition def) => def.Name;
            public int GetX(BuildingDefinition def, BuildingState state) => def.X;
            public int GetY(BuildingDefinition def, BuildingState state) => def.Y;
            public bool IncludeRow(BuildingDefinition def, BuildingState state) => true;

            public void WriteRow(TimberbotJw jw, string format, bool fullDetail, BuildingDefinition d, BuildingState s, BuildingDetailState detailState)
            {
                jw.OpenObj()
                    .Prop("id", d.Id)
                    .Prop("name", d.Name)
                    .Prop("x", d.X).Prop("y", d.Y).Prop("z", d.Z)
                    .Prop("orientation", d.Orientation ?? "")
                    .Prop("finished", s.Finished)
                    .Prop("paused", s.Paused);

                if (!fullDetail)
                {
                    jw.Prop("priority", s.ConstructionPriority ?? "")
                        .Prop("workers", s.MaxWorkers > 0 ? $"{s.AssignedWorkers}/{s.DesiredWorkers}" : "")
                        .CloseObj();
                    return;
                }

                jw.Prop("constructionPriority", s.ConstructionPriority ?? "")
                    .Prop("workplacePriority", s.WorkplacePriorityStr ?? "")
                    .Prop("maxWorkers", s.MaxWorkers)
                    .Prop("desiredWorkers", s.DesiredWorkers)
                    .Prop("assignedWorkers", s.AssignedWorkers)
                    .Prop("reachable", s.Reachable)
                    .Prop("powered", s.Powered)
                    .Prop("isGenerator", d.IsGenerator)
                    .Prop("isConsumer", d.IsConsumer)
                    .Prop("nominalPowerInput", d.NominalPowerInput)
                    .Prop("nominalPowerOutput", d.NominalPowerOutput)
                    .Prop("powerDemand", s.PowerDemand)
                    .Prop("powerSupply", s.PowerSupply)
                    .Prop("buildProgress", s.BuildProgress)
                    .Prop("materialProgress", s.MaterialProgress)
                    .Prop("hasMaterials", s.HasMaterials)
                    .Prop("stock", s.Stock)
                    .Prop("capacity", s.Capacity)
                    .Prop("dwellers", s.Dwellers)
                    .Prop("maxDwellers", s.MaxDwellers)
                    .Prop("floodgate", d.HasFloodgate)
                    .Prop("height", d.HasFloodgate != 0 ? s.FloodgateHeight : 0f, "F1")
                    .Prop("maxHeight", d.HasFloodgate != 0 ? d.FloodgateMaxHeight : 0f, "F1")
                    .Prop("isClutch", d.HasClutch)
                    .Prop("clutchEngaged", s.ClutchEngaged)
                    .Prop("currentRecipe", s.CurrentRecipe ?? "")
                    .Prop("productionProgress", s.ProductionProgress)
                    .Prop("readyToProduce", s.ReadyToProduce)
                    .Prop("effectRadius", d.EffectRadius)
                    .Prop("isWonder", d.HasWonder)
                    .Prop("wonderActive", s.WonderActive);

                if (format == "toon")
                {
                    jw.Prop("inventory", detailState?.InventoryToon ?? "")
                        .Prop("recipes", detailState?.RecipesToon ?? "");
                }
                else
                {
                    jw.Obj("inventory");
                    if (detailState?.Inventory != null)
                        foreach (var kvp in detailState.Inventory)
                            jw.Key(kvp.Key).Int(kvp.Value);
                    jw.CloseObj();
                    jw.Arr("recipes");
                    if (detailState?.Recipes != null)
                        for (int ri = 0; ri < detailState.Recipes.Count; ri++)
                            jw.Str(detailState.Recipes[ri]);
                    jw.CloseArr();
                }
                jw.CloseObj();
            }
        }

        private sealed class SettlementSnapshot { public string Name; }
        private sealed class TimeSnapshot { public int DayNumber; public float DayProgress; public float PartialDayNumber; }
        private sealed class WeatherSnapshot
        {
            public int Cycle;
            public int CycleDay;
            public bool IsHazardous;
            public int TemperateWeatherDuration;
            public int HazardousWeatherDuration;
            public int CycleLengthInDays;
        }
        private sealed class SpeedSnapshot { public int Speed; }
        private sealed class WorkHoursSnapshot { public float EndHours; public bool AreWorkingHours; }
        private sealed class RawJsonSnapshot { public string Json; }
        private sealed class NotificationItem { public string Subject; public string Description; public int Cycle; public int CycleDay; }
        private sealed class AlertItem { public string Type; public int Id; public string Name; public string Workers; }
        private sealed class PowerBuildingItem { public string Name; public int Id; public int IsGenerator; public int NominalOutput; public int NominalInput; }
        private sealed class PowerNetworkItem { public int Id; public int Supply; public int Demand; public PowerBuildingItem[] Buildings; }
        private sealed class PowerNetworkBuilder { public int Id; public int Supply; public int Demand; public List<PowerBuildingItem> Buildings; }

        private interface IValueSchema<TSnapshot>
        {
            void Write(TimberbotJw jw, string format, TSnapshot snapshot);
        }

        private sealed class ValueStore<TSnapshot> where TSnapshot : class
        {
            public delegate TSnapshot BuildSnapshot();

            private readonly object _lock = new object();
            private bool _refreshRequested;
            private readonly List<Waiter> _waiters = new List<Waiter>();
            private readonly List<Waiter> _wakeBatch = new List<Waiter>();
            private TSnapshot _published;

            public void ProcessPending(float now, BuildSnapshot build)
            {
                lock (_lock)
                {
                    if (!_refreshRequested) return;
                    _refreshRequested = false;
                    _wakeBatch.Clear();
                    _wakeBatch.AddRange(_waiters);
                    _waiters.Clear();
                }

                try { _published = build(); }
                finally
                {
                    for (int i = 0; i < _wakeBatch.Count; i++)
                        _wakeBatch[i].Signal.Set();
                }
            }

            public TSnapshot RequestFresh(int timeoutMs)
            {
                var waiter = new Waiter();
                lock (_lock)
                {
                    _refreshRequested = true;
                    _waiters.Add(waiter);
                }
                if (!waiter.Signal.Wait(timeoutMs))
                {
                    lock (_lock) _waiters.Remove(waiter);
                    throw new TimeoutException();
                }
                return _published;
            }

            public void PublishNow(float now, BuildSnapshot build)
            {
                _published = build();
            }

            private sealed class Waiter
            {
                public readonly ManualResetEventSlim Signal = new ManualResetEventSlim(false);
            }
        }

        private sealed class ValueRoute<TSnapshot> where TSnapshot : class
        {
            private readonly TimberbotJw _jw;
            private readonly Func<int, TSnapshot> _snapshotProvider;
            private readonly IValueSchema<TSnapshot> _schema;

            public ValueRoute(TimberbotJw jw, Func<int, TSnapshot> snapshotProvider, IValueSchema<TSnapshot> schema)
            {
                _jw = jw;
                _snapshotProvider = snapshotProvider;
                _schema = schema;
            }

            public object Collect(string format = "toon")
            {
                TSnapshot snapshot;
                try { snapshot = _snapshotProvider(2000); }
                catch (TimeoutException) { return _jw.Error("refresh_timeout"); }
                if (snapshot == null) return _jw.Error("not_ready");
                var jw = _jw.Reset();
                _schema.Write(jw, format ?? "toon", snapshot);
                return jw.ToString();
            }
        }

        private interface IFlatArraySchema<TItem>
        {
            void WriteItem(TimberbotJw jw, string format, TItem item);
        }

        private sealed class FlatArrayRoute<TItem>
        {
            private readonly TimberbotJw _jw;
            private readonly Func<int, TItem[]> _itemsProvider;
            private readonly IFlatArraySchema<TItem> _schema;

            public FlatArrayRoute(TimberbotJw jw, Func<int, TItem[]> itemsProvider, IFlatArraySchema<TItem> schema)
            {
                _jw = jw;
                _itemsProvider = itemsProvider;
                _schema = schema;
            }

            public object Collect(string format = "toon", int limit = 100, int offset = 0)
            {
                TItem[] items;
                try { items = _itemsProvider(2000); }
                catch (TimeoutException) { return _jw.Error("refresh_timeout"); }
                if (items == null) return _jw.Error("not_ready");

                bool paginated = limit > 0;
                int skipped = 0, emitted = 0;
                var jw = _jw.BeginArr();
                for (int i = 0; i < items.Length; i++)
                {
                    if (offset > 0 && skipped < offset) { skipped++; continue; }
                    if (paginated && emitted >= limit) break;
                    emitted++;
                    _schema.WriteItem(jw, format ?? "toon", items[i]);
                }
                return jw.End();
            }
        }

        private sealed class TimeSchema : IValueSchema<TimeSnapshot>
        {
            public void Write(TimberbotJw jw, string format, TimeSnapshot snapshot)
                => jw.OpenObj()
                    .Prop("dayNumber", snapshot.DayNumber)
                    .Prop("dayProgress", snapshot.DayProgress)
                    .Prop("partialDayNumber", snapshot.PartialDayNumber)
                    .CloseObj();
        }

        private sealed class WeatherSchema : IValueSchema<WeatherSnapshot>
        {
            public void Write(TimberbotJw jw, string format, WeatherSnapshot snapshot)
                => jw.OpenObj()
                    .Prop("cycle", snapshot.Cycle)
                    .Prop("cycleDay", snapshot.CycleDay)
                    .Prop("isHazardous", snapshot.IsHazardous)
                    .Prop("temperateWeatherDuration", snapshot.TemperateWeatherDuration)
                    .Prop("hazardousWeatherDuration", snapshot.HazardousWeatherDuration)
                    .Prop("cycleLengthInDays", snapshot.CycleLengthInDays)
                    .CloseObj();
        }

        private sealed class SpeedSchema : IValueSchema<SpeedSnapshot>
        {
            public void Write(TimberbotJw jw, string format, SpeedSnapshot snapshot)
                => jw.OpenObj().Prop("speed", snapshot.Speed).CloseObj();
        }

        private sealed class WorkHoursSchema : IValueSchema<WorkHoursSnapshot>
        {
            public void Write(TimberbotJw jw, string format, WorkHoursSnapshot snapshot)
                => jw.OpenObj().Prop("endHours", snapshot.EndHours).Prop("areWorkingHours", snapshot.AreWorkingHours).CloseObj();
        }

        private sealed class RawJsonSchema : IValueSchema<RawJsonSnapshot>
        {
            public void Write(TimberbotJw jw, string format, RawJsonSnapshot snapshot)
                => jw.Raw(snapshot.Json ?? "null");
        }

        private sealed class NotificationSchema : IFlatArraySchema<NotificationItem>
        {
            public void WriteItem(TimberbotJw jw, string format, NotificationItem item)
                => jw.OpenObj()
                    .Prop("subject", item.Subject)
                    .Prop("description", item.Description)
                    .Prop("cycle", item.Cycle)
                    .Prop("cycleDay", item.CycleDay)
                    .CloseObj();
        }

        private sealed class AlertSchema : IFlatArraySchema<AlertItem>
        {
            public void WriteItem(TimberbotJw jw, string format, AlertItem item)
            {
                jw.OpenObj().Prop("type", item.Type).Prop("id", item.Id).Prop("name", item.Name);
                if (!string.IsNullOrEmpty(item.Workers))
                    jw.Prop("workers", item.Workers);
                jw.CloseObj();
            }
        }

        private sealed class PowerSchema : IValueSchema<PowerNetworkItem[]>
        {
            public void Write(TimberbotJw jw, string format, PowerNetworkItem[] snapshot)
            {
                jw.OpenArr();
                if (snapshot != null)
                {
                    for (int i = 0; i < snapshot.Length; i++)
                    {
                        var net = snapshot[i];
                        jw.OpenObj().Prop("id", net.Id).Prop("supply", net.Supply).Prop("demand", net.Demand);
                        jw.Arr("buildings");
                        if (net.Buildings != null)
                        {
                            for (int bi = 0; bi < net.Buildings.Length; bi++)
                            {
                                var building = net.Buildings[bi];
                                jw.OpenObj()
                                    .Prop("name", building.Name)
                                    .Prop("id", building.Id)
                                    .Prop("isGenerator", building.IsGenerator)
                                    .Prop("nominalOutput", building.NominalOutput)
                                    .Prop("nominalInput", building.NominalInput)
                                    .CloseObj();
                            }
                        }
                        jw.CloseArr().CloseObj();
                    }
                }
                jw.CloseArr();
            }
        }
    }
}
