// TimberbotHttpServer.cs -- HTTP server and request routing.
//
// Runs an HttpListener on a background thread (port 8085 by default).
// Threading model:
//   GET requests  -> served directly on the listener thread from ReadV2 snapshots
//   POST requests -> queued to ConcurrentQueue, drained on Unity main thread (max 10/frame)
//
// This split exists because Unity game services are single-threaded. GET endpoints
// read only published snapshots or explicit thread-safe game services, while POST
// endpoints call live game services on the main thread.
//
// All responses are JSON. The server serializes whatever object TimberbotService
// returns using Newtonsoft.Json. TOON format is handled by TimberbotService returning
// pre-built strings for endpoints that support it.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Timberbot
{
    // HTTP server on port 8085. Background listener thread queues requests,
    // Unity main thread drains them (max 10/frame). ping + speed answered on listener thread.
    // format param: ?format=toon (default) = flat, ?format=json = full nested
    class TimberbotHttpServer
    {
        private readonly TimberbotService _service;
        private readonly HttpListener _listener;
        private readonly Thread _listenerThread;
        // thread-safe queue: background listener thread enqueues, Unity main thread dequeues
        private readonly ConcurrentQueue<PendingRequest> _pending = new ConcurrentQueue<PendingRequest>();
        private readonly Queue<PendingRequest> _writeQueue = new Queue<PendingRequest>();
        private PendingRequest _activeWriteRequest;
        private ITimberbotWriteJob _activeWriteJob;
        // volatile: read by main thread, written by Stop(). No lock needed for bool.
        private volatile bool _running;
        private readonly bool _debugEnabled;
        // separate JW for HTTP-layer errors (thread-safe: not shared with read/write paths)
        private readonly TimberbotJw _jw = new TimberbotJw(512);

        // Captures everything needed to process a POST request on the main thread.
        // The JSON body is parsed on the listener thread (cheap) so the main thread
        // only does game state mutations (expensive, must be single-threaded).
        class PendingRequest
        {
            public HttpListenerContext Context;  // HTTP response handle
            public string Route;                 // e.g. "/api/building/pause"
            public string Method;                // "POST" (GET never queued)
            public JObject Body;                 // parsed JSON body (null if no body)
            public string Format;                // "toon" or "json" (response format)
            public string Detail;                // "basic" or "full" (response detail level)
            public int Limit;                    // max items to return (0 = unlimited, default 100)
            public int Offset;                   // skip first N items
            public string FilterName;            // name substring filter (case-insensitive)
            public int FilterX, FilterY, FilterRadius; // proximity filter (Manhattan distance)
            public long QueuedAtTicks;           // listener-thread enqueue timestamp
            public int QueuedAtFrame;            // main-thread queue admission frame
        }

        public TimberbotHttpServer(int port, TimberbotService service, bool debugEnabled = false)
        {
            _service = service;
            _debugEnabled = debugEnabled;
            _listener = new HttpListener();

            // Try wildcard binding first (http://+:port/) which accepts connections
            // from any interface (LAN, WSL, etc). Requires admin/URL reservation on Windows.
            // Falls back to localhost-only if that fails (no admin needed).
            try
            {
                _listener.Prefixes.Add($"http://+:{port}/");
                _listener.Start();
            }
            catch (HttpListenerException)
            {
                TimberbotLog.Info($"port +:{port} failed, falling back to localhost");
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
            }

            _running = true;
            _listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "Timberbot-HTTP" };
            _listenerThread.Start();
        }

        public void Stop()
        {
            _running = false;
            FailOutstanding("operation_failed: server_stopped");
            try { _listener?.Stop(); } catch { }
        }

        // Called every frame from UpdateSingleton on the Unity main thread.
        // Admits up to 10 queued POST requests per frame.
        public void DrainRequests()
        {
            int processed = 0;
            while (processed < 10 && _pending.TryDequeue(out var req))
            {
                processed++;
                try
                {
                    if (ShouldQueueWrite(req.Route, req.Method))
                    {
                        req.QueuedAtFrame = UnityEngine.Time.frameCount;
                        _writeQueue.Enqueue(req);
                    }
                    else
                    {
                        var data = RouteRequest(req.Route, req.Method, req.Body, req.Format, req.Detail, req.Limit, req.Offset, req.FilterName, req.FilterX, req.FilterY, req.FilterRadius);
                        RespondAsync(req.Context, 200, data);
                    }
                }
                catch (Exception ex)
                {
                    TimberbotLog.Error("route.post", ex);
                    RespondAsync(req.Context, 500, _jw.Error("internal_error: " + ex.Message.Replace("\"", "'").Replace("\r", "").Replace("\n", " | ")));
                }
            }
        }

        // Called every frame. Steps the active write job forward until it completes
        // or the per-frame budget (default 2ms) runs out. When the job completes,
        // sends the HTTP response back to the waiting client.
        public void ProcessWriteJobs(float now, double budgetMs)
        {
            if (!_running) return;

            if (_activeWriteJob == null && _writeQueue.Count > 0)
            {
                _activeWriteRequest = _writeQueue.Dequeue();
                try
                {
                    _activeWriteJob = CreateWriteJob(_activeWriteRequest);
                }
                catch (Exception ex)
                {
                    TimberbotLog.Error("write.admit", ex);
                    RespondAsync(_activeWriteRequest.Context, 500, _jw.Error("internal_error: " + ex.Message.Replace("\"", "'").Replace("\r", "").Replace("\n", " | ")));
                    _activeWriteRequest = null;
                    _activeWriteJob = null;
                }
            }

            if (_activeWriteJob == null) return;

            try
            {
                _activeWriteJob.Step(now, budgetMs);
                if (_activeWriteJob.IsCompleted)
                {
                    RespondAsync(_activeWriteRequest.Context, _activeWriteJob.StatusCode, _activeWriteJob.Result);
                    _activeWriteRequest = null;
                    _activeWriteJob = null;
                }
            }
            catch (Exception ex)
            {
                TimberbotLog.Error("write.step", ex);
                RespondAsync(_activeWriteRequest.Context, 500, _jw.Error("internal_error: " + ex.Message.Replace("\"", "'").Replace("\r", "").Replace("\n", " | ")));
                _activeWriteRequest = null;
                _activeWriteJob = null;
            }
        }

        private void ListenLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch
                {
                    if (!_running) break;
                    continue;
                }

                var path = ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
                var method = ctx.Request.HttpMethod.ToUpperInvariant();

                if (path == "/api/ping")
                {
                    Respond(ctx, 200, "{\"status\":\"ok\",\"ready\":true}");
                    continue;
                }
                if (path == "/api/settlement")
                {
                    Respond(ctx, 200, "{\"name\":\"" + _service.ReadV2.GetSettlementName().Replace("\"", "\\\"") + "\"}");
                    continue;
                }

                // format: "toon" = flat key:value pairs (default, human-readable)
                //         "json" = full nested JSON (machine-parseable)
                // detail: "basic" = compact fields, "full" = all fields including inventory/needs
                var format = ctx.Request.QueryString["format"] ?? "toon";
                var detail = ctx.Request.QueryString["detail"] ?? "basic";
                // pagination: limit=100 default (0=unlimited), offset=0 default
                int.TryParse(ctx.Request.QueryString["limit"], out int limit);
                int.TryParse(ctx.Request.QueryString["offset"], out int offset);
                if (ctx.Request.QueryString["limit"] == null) limit = 100;
                // server-side filtering: name (substring), x/y/radius (proximity)
                var filterName = ctx.Request.QueryString["name"];
                int.TryParse(ctx.Request.QueryString["x"], out int filterX);
                int.TryParse(ctx.Request.QueryString["y"], out int filterY);
                int.TryParse(ctx.Request.QueryString["radius"], out int filterRadius);
                // GET requests: handled on the background listener thread and served from
                // ReadV2's published snapshots or explicit thread-safe game services.
                if (method == "GET")
                {
                    try
                    {
                        var data = RouteRequest(path, method, null, format, detail, limit, offset, filterName, filterX, filterY, filterRadius);
                        Respond(ctx, 200, data);
                    }
                    catch (Exception ex)
                    {
                        TimberbotLog.Error("route.get", ex);
                        Respond(ctx, 500, _jw.Error("internal_error: " + ex.Message.Replace("\"", "'").Replace("\r", "").Replace("\n", " | ")));
                    }
                    continue;
                }

                // POST requests: can't execute on this thread because Unity game services
                // are single-threaded. Instead, we parse the JSON body here, then queue the
                // request to a ConcurrentQueue. The main thread drains up to 10 queued requests
                // per frame in DrainRequests() (called from UpdateSingleton).
                JObject body = null;
                if (ctx.Request.HasEntityBody)
                {
                    try
                    {
                        using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                        {
                            var raw = reader.ReadToEnd();
                            body = JObject.Parse(raw);
                        }
                    }
                    catch
                    {
                        Respond(ctx, 400, _jw.Error("invalid_body"));
                        continue;
                    }
                }

                // POST requests can override format/detail/limit/offset in the JSON body too
                // (body takes priority over query string)
                format = body?.Value<string>("format") ?? format;
                detail = body?.Value<string>("detail") ?? detail;
                if (body?["limit"] != null) limit = body.Value<int>("limit");
                if (body?["offset"] != null) offset = body.Value<int>("offset");
                if (body?["name"] != null) filterName = body.Value<string>("name");
                if (body?["x"] != null) filterX = body.Value<int>("x");
                if (body?["y"] != null) filterY = body.Value<int>("y");
                if (body?["radius"] != null) filterRadius = body.Value<int>("radius");

                _pending.Enqueue(new PendingRequest
                {
                    Context = ctx,
                    Route = path,
                    Method = method,
                    Body = body,
                    Format = format,
                    Detail = detail,
                    Limit = limit,
                    Offset = offset,
                    FilterName = filterName,
                    FilterX = filterX,
                    FilterY = filterY,
                    FilterRadius = filterRadius,
                    QueuedAtTicks = System.Diagnostics.Stopwatch.GetTimestamp()
                });
            }
        }

        // Central routing table. Maps HTTP method + path to service method calls.
        // GET endpoints return ReadV2 snapshot data (thread-safe, called from background thread).
        // POST endpoints mutate game state (called from main thread via DrainRequests).
        //
        // Notable exceptions to the GET=read/POST=write convention:
        //   /api/tiles (POST): reads terrain data but needs body params for the region
        //   /api/building/range (POST): reads work radius but needs body param for building ID
        //   /api/placement/find (POST): reads valid spots but needs body params for search area
        // These are logically reads but use POST because GET has no request body.
        private object RouteRequest(string path, string method, JObject body, string format = "toon", string detail = "basic", int limit = 100, int offset = 0, string filterName = null, int filterX = 0, int filterY = 0, int filterRadius = 0)
        {
            // GET endpoints (read from ReadV2 snapshots -- zero contention with game thread)
            if (method == "GET")
            {
                switch (path)
                {
                    case "/api/summary":
                        return _service.ReadV2.CollectSummary(format);
                    case "/api/alerts":
                        return _service.ReadV2.CollectAlerts(format, limit, offset);
                    case "/api/tree_clusters":
                        return _service.ReadV2.CollectTreeClusters(format);
                    case "/api/food_clusters":
                        return _service.ReadV2.CollectFoodClusters(format);
                    case "/api/resources":
                        return _service.ReadV2.CollectResources(format);
                    case "/api/population":
                        return _service.ReadV2.CollectPopulation();
                    case "/api/time":
                        return _service.ReadV2.CollectTime();
                    case "/api/weather":
                        return _service.ReadV2.CollectWeather();
                    case "/api/districts":
                        return _service.ReadV2.CollectDistricts(format);
                    case "/api/buildings":
                        return _service.ReadV2.CollectBuildings(format, detail, limit, offset, filterName, filterX, filterY, filterRadius);
                    case "/api/trees":
                        return _service.ReadV2.CollectTrees(format, limit, offset, filterName, filterX, filterY, filterRadius);
                    case "/api/crops":
                        return _service.ReadV2.CollectCrops(format, limit, offset, filterName, filterX, filterY, filterRadius);
                    case "/api/gatherables":
                        return _service.ReadV2.CollectGatherables(format, limit, offset, filterName, filterX, filterY, filterRadius);
                    case "/api/beavers":
                        return _service.ReadV2.CollectBeavers(format, detail, limit, offset, filterName, filterX, filterY, filterRadius);
                    case "/api/distribution":
                        return _service.ReadV2.CollectDistribution(format);
                    case "/api/science":
                        return _service.ReadV2.CollectScience(format);
                    case "/api/wellbeing":
                        return _service.ReadV2.CollectWellbeing(format);
                    case "/api/notifications":
                        return _service.ReadV2.CollectNotifications(format, limit, offset);
                    case "/api/workhours":
                        return _service.ReadV2.CollectWorkHours();
                    case "/api/power":
                        return _service.ReadV2.CollectPowerNetworks(format);
                    case "/api/speed":
                        return _service.ReadV2.CollectSpeed();
                    case "/api/prefabs":
                        return _service.Placement.CollectPrefabs();
                    case "/api/webhooks":
                        return _service.WebhookMgr.ListWebhooks();
                }
            }

            // POST endpoints (write -- executed on Unity main thread via queue)
            if (method == "POST")
            {
                switch (path)
                {
                    case "/api/speed":
                        return _service.Write.SetSpeed(body?.Value<int>("speed") ?? 0);
                    case "/api/building/pause":
                        return _service.Write.PauseBuilding(
                            body?.Value<int>("id") ?? 0,
                            body?.Value<bool>("paused") ?? false);
                    case "/api/building/clutch":
                        return _service.Write.SetClutch(
                            body?.Value<int>("id") ?? 0,
                            body?.Value<bool>("engaged") ?? true);
                    case "/api/building/floodgate":
                        return _service.Write.SetFloodgateHeight(
                            body?.Value<int>("id") ?? 0,
                            body?.Value<float>("height") ?? 0f);
                    case "/api/building/priority":
                        return _service.Write.SetBuildingPriority(
                            body?.Value<int>("id") ?? 0,
                            body?.Value<string>("priority") ?? "Normal",
                            body?.Value<string>("type") ?? "");
                    case "/api/building/hauling":
                        return _service.Write.SetHaulPriority(
                            body?.Value<int>("id") ?? 0,
                            body?.Value<bool>("prioritized") ?? true);
                    case "/api/building/recipe":
                        return _service.Write.SetRecipe(
                            body?.Value<int>("id") ?? 0,
                            body?.Value<string>("recipe") ?? "");
                    case "/api/building/farmhouse":
                        return _service.Write.SetFarmhouseAction(
                            body?.Value<int>("id") ?? 0,
                            body?.Value<string>("action") ?? "");
                    case "/api/building/plantable":
                        return _service.Write.SetPlantablePriority(
                            body?.Value<int>("id") ?? 0,
                            body?.Value<string>("plantable") ?? "");
                    case "/api/building/workers":
                        return _service.Write.SetWorkers(
                            body?.Value<int>("id") ?? 0,
                            body?.Value<int>("count") ?? 0);
                    case "/api/planting/mark":
                        return _service.Write.MarkPlanting(
                            body?.Value<int>("x1") ?? 0,
                            body?.Value<int>("y1") ?? 0,
                            body?.Value<int>("x2") ?? 0,
                            body?.Value<int>("y2") ?? 0,
                            body?.Value<int>("z") ?? 0,
                            body?.Value<string>("crop") ?? "");
                    case "/api/planting/find":
                        return _service.Write.FindPlantingSpots(
                            body?.Value<string>("crop") ?? "",
                            body?.Value<int>("building_id") ?? 0,
                            body?.Value<int>("x1") ?? 0,
                            body?.Value<int>("y1") ?? 0,
                            body?.Value<int>("x2") ?? 0,
                            body?.Value<int>("y2") ?? 0,
                            body?.Value<int>("z") ?? 0);
                    case "/api/building/range":
                        return _service.Write.CollectBuildingRange(
                            body?.Value<int>("id") ?? 0);
                    case "/api/planting/clear":
                        return _service.Write.UnmarkPlanting(
                            body?.Value<int>("x1") ?? 0,
                            body?.Value<int>("y1") ?? 0,
                            body?.Value<int>("x2") ?? 0,
                            body?.Value<int>("y2") ?? 0,
                            body?.Value<int>("z") ?? 0);
                    case "/api/cutting/area":
                        return _service.Write.MarkCuttingArea(
                            body?.Value<int>("x1") ?? 0,
                            body?.Value<int>("y1") ?? 0,
                            body?.Value<int>("x2") ?? 0,
                            body?.Value<int>("y2") ?? 0,
                            body?.Value<int>("z") ?? 0,
                            body?.Value<bool>("marked") ?? true);
                    case "/api/stockpile/capacity":
                        return _service.Write.SetStockpileCapacity(
                            body?.Value<int>("id") ?? 0,
                            body?.Value<int>("capacity") ?? 0);
                    case "/api/stockpile/good":
                        return _service.Write.SetStockpileGood(
                            body?.Value<int>("id") ?? 0,
                            body?.Value<string>("good") ?? "");
                    case "/api/workhours":
                        return _service.Write.SetWorkHours(
                            body?.Value<int>("endHours") ?? 16);
                    case "/api/district/migrate":
                        return _service.Write.MigratePopulation(
                            body?.Value<string>("from") ?? "",
                            body?.Value<string>("to") ?? "",
                            body?.Value<int>("count") ?? 1);
                    case "/api/science/unlock":
                        return _service.Write.UnlockBuilding(
                            body?.Value<string>("building") ?? "");
                    case "/api/distribution":
                        return _service.Write.SetDistribution(
                            body?.Value<string>("district") ?? "",
                            body?.Value<string>("good") ?? "",
                            body?.Value<string>("import") ?? "",
                            body?.Value<int>("exportThreshold") ?? -1);
                    case "/api/tiles":
                        return _service.ReadV2.CollectTiles(
                            format,
                            body?.Value<int>("x1") ?? 0,
                            body?.Value<int>("y1") ?? 0,
                            body?.Value<int>("x2") ?? 0,
                            body?.Value<int>("y2") ?? 0);
                    case "/api/building/demolish":
                        return _service.Placement.DemolishBuilding(
                            body?.Value<int>("id") ?? 0);
                    case "/api/crop/demolish":
                        return _service.Placement.DemolishCrop(
                            body?.Value<int>("id") ?? 0);
                    case "/api/webhooks":
                        return _service.WebhookMgr.RegisterWebhook(
                            body?.Value<string>("url") ?? "",
                            body?["events"]?.ToObject<System.Collections.Generic.List<string>>());
                    case "/api/webhooks/delete":
                        return _service.WebhookMgr.UnregisterWebhook(
                            body?.Value<string>("id") ?? "");
                    case "/api/debug":
                        if (!_debugEnabled) return _jw.Error("disabled: debug endpoint");
                        var debugArgs = new System.Collections.Generic.Dictionary<string, string>();
                        if (body != null)
                            foreach (var prop in body.Properties())
                                debugArgs[prop.Name] = prop.Value?.ToString() ?? "";
                        return _service.DebugTool.DebugInspect(
                            body?.Value<string>("target") ?? "help", debugArgs);
                    case "/api/benchmark":
                        if (!_debugEnabled) return _jw.Error("disabled: benchmark endpoint");
                        return _service.DebugTool.RunBenchmark(
                            body?.Value<int>("iterations") ?? 100);
                    case "/api/path/place":
                        return _service.Placement.RoutePath(
                            body?.Value<int>("x1") ?? 0,
                            body?.Value<int>("y1") ?? 0,
                            body?.Value<int>("x2") ?? 0,
                            body?.Value<int>("y2") ?? 0,
                            body?.Value<string>("style") ?? "direct",
                            body?.Value<int>("sections") ?? 0,
                            body?.Value<bool?>("timings") ?? false);
                    case "/api/placement/find":
                        return _service.Placement.FindPlacement(
                            body?.Value<string>("prefab") ?? "",
                            body?.Value<int>("x1") ?? 0,
                            body?.Value<int>("y1") ?? 0,
                            body?.Value<int>("x2") ?? 0,
                            body?.Value<int>("y2") ?? 0);
                    case "/api/building/place":
                        return _service.Placement.PlaceBuilding(
                            body?.Value<string>("prefab") ?? "",
                            body?.Value<int>("x") ?? 0,
                            body?.Value<int>("y") ?? 0,
                            body?.Value<int>("z") ?? 0,
                            body?.Value<string>("orientation") ?? "south")
                            .ToJson(_service.Placement.Jw);
                }
            }

            return _jw.Error("unknown_endpoint", ("endpoints", new[] {
                "GET /api/ping", "GET /api/summary",
                "GET /api/buildings", "GET /api/trees",
                "GET /api/beavers", "GET /api/resources", "GET /api/districts", "GET /api/weather",
                "GET /api/time", "GET /api/speed", "GET /api/prefabs", "GET /api/power",
                "POST /api/speed", "POST /api/building/place", "POST /api/building/demolish"
            }));
        }

        private bool ShouldQueueWrite(string path, string method)
        {
            if (method != "POST") return false;
            switch (path)
            {
                case "/api/tiles":
                case "/api/building/range":
                case "/api/placement/find":
                case "/api/planting/find":
                case "/api/debug":
                case "/api/benchmark":
                    return false;
                default:
                    return true;
            }
        }

        private ITimberbotWriteJob CreateWriteJob(PendingRequest req)
        {
            var body = req.Body;
            switch (req.Route)
            {
                case "/api/path/place":
                    return _service.Placement.CreateRoutePathJob(
                        body?.Value<int>("x1") ?? 0,
                        body?.Value<int>("y1") ?? 0,
                        body?.Value<int>("x2") ?? 0,
                        body?.Value<int>("y2") ?? 0,
                        body?.Value<string>("style") ?? "direct",
                        body?.Value<int>("sections") ?? 0,
                        body?.Value<bool?>("timings") ?? false,
                        req.QueuedAtTicks,
                        req.QueuedAtFrame);
                case "/api/building/place":
                    return new LambdaWriteJob(req.Route, () =>
                        _service.Placement.PlaceBuilding(
                            body?.Value<string>("prefab") ?? "",
                            body?.Value<int>("x") ?? 0,
                            body?.Value<int>("y") ?? 0,
                            body?.Value<int>("z") ?? 0,
                            body?.Value<string>("orientation") ?? "south")
                        .ToJson(_service.Placement.Jw));
                case "/api/building/demolish":
                    return new LambdaWriteJob(req.Route, () => _service.Placement.DemolishBuilding(body?.Value<int>("id") ?? 0));
                case "/api/crop/demolish":
                    return new LambdaWriteJob(req.Route, () => _service.Placement.DemolishCrop(body?.Value<int>("id") ?? 0));
                case "/api/speed":
                    return new LambdaWriteJob(req.Route, () => _service.Write.SetSpeed(body?.Value<int>("speed") ?? 0));
                case "/api/building/pause":
                    return new LambdaWriteJob(req.Route, () => _service.Write.PauseBuilding(body?.Value<int>("id") ?? 0, body?.Value<bool>("paused") ?? false));
                case "/api/building/clutch":
                    return new LambdaWriteJob(req.Route, () => _service.Write.SetClutch(body?.Value<int>("id") ?? 0, body?.Value<bool>("engaged") ?? true));
                case "/api/building/floodgate":
                    return new LambdaWriteJob(req.Route, () => _service.Write.SetFloodgateHeight(body?.Value<int>("id") ?? 0, body?.Value<float>("height") ?? 0f));
                case "/api/building/priority":
                    return new LambdaWriteJob(req.Route, () => _service.Write.SetBuildingPriority(body?.Value<int>("id") ?? 0, body?.Value<string>("priority") ?? "Normal", body?.Value<string>("type") ?? ""));
                case "/api/building/hauling":
                    return new LambdaWriteJob(req.Route, () => _service.Write.SetHaulPriority(body?.Value<int>("id") ?? 0, body?.Value<bool>("prioritized") ?? true));
                case "/api/building/recipe":
                    return new LambdaWriteJob(req.Route, () => _service.Write.SetRecipe(body?.Value<int>("id") ?? 0, body?.Value<string>("recipe") ?? ""));
                case "/api/building/farmhouse":
                    return new LambdaWriteJob(req.Route, () => _service.Write.SetFarmhouseAction(body?.Value<int>("id") ?? 0, body?.Value<string>("action") ?? ""));
                case "/api/building/plantable":
                    return new LambdaWriteJob(req.Route, () => _service.Write.SetPlantablePriority(body?.Value<int>("id") ?? 0, body?.Value<string>("plantable") ?? ""));
                case "/api/building/workers":
                    return new LambdaWriteJob(req.Route, () => _service.Write.SetWorkers(body?.Value<int>("id") ?? 0, body?.Value<int>("count") ?? 0));
                case "/api/planting/mark":
                    return new LambdaWriteJob(req.Route, () => _service.Write.MarkPlanting(body?.Value<int>("x1") ?? 0, body?.Value<int>("y1") ?? 0, body?.Value<int>("x2") ?? 0, body?.Value<int>("y2") ?? 0, body?.Value<int>("z") ?? 0, body?.Value<string>("crop") ?? ""));
                case "/api/planting/clear":
                    return new LambdaWriteJob(req.Route, () => _service.Write.UnmarkPlanting(body?.Value<int>("x1") ?? 0, body?.Value<int>("y1") ?? 0, body?.Value<int>("x2") ?? 0, body?.Value<int>("y2") ?? 0, body?.Value<int>("z") ?? 0));
                case "/api/cutting/area":
                    return new LambdaWriteJob(req.Route, () => _service.Write.MarkCuttingArea(body?.Value<int>("x1") ?? 0, body?.Value<int>("y1") ?? 0, body?.Value<int>("x2") ?? 0, body?.Value<int>("y2") ?? 0, body?.Value<int>("z") ?? 0, body?.Value<bool>("marked") ?? true));
                case "/api/stockpile/capacity":
                    return new LambdaWriteJob(req.Route, () => _service.Write.SetStockpileCapacity(body?.Value<int>("id") ?? 0, body?.Value<int>("capacity") ?? 0));
                case "/api/stockpile/good":
                    return new LambdaWriteJob(req.Route, () => _service.Write.SetStockpileGood(body?.Value<int>("id") ?? 0, body?.Value<string>("good") ?? ""));
                case "/api/workhours":
                    return new LambdaWriteJob(req.Route, () => _service.Write.SetWorkHours(body?.Value<int>("endHours") ?? 16));
                case "/api/district/migrate":
                    return new LambdaWriteJob(req.Route, () => _service.Write.MigratePopulation(body?.Value<string>("from") ?? "", body?.Value<string>("to") ?? "", body?.Value<int>("count") ?? 1));
                case "/api/science/unlock":
                    return new LambdaWriteJob(req.Route, () => _service.Write.UnlockBuilding(body?.Value<string>("building") ?? ""));
                case "/api/distribution":
                    return new LambdaWriteJob(req.Route, () => _service.Write.SetDistribution(body?.Value<string>("district") ?? "", body?.Value<string>("good") ?? "", body?.Value<string>("import") ?? "", body?.Value<int>("exportThreshold") ?? -1));
                case "/api/webhooks":
                    return new LambdaWriteJob(req.Route, () => _service.WebhookMgr.RegisterWebhook(body?.Value<string>("url") ?? "", body?["events"]?.ToObject<List<string>>()));
                case "/api/webhooks/delete":
                    return new LambdaWriteJob(req.Route, () => _service.WebhookMgr.UnregisterWebhook(body?.Value<string>("id") ?? ""));
                default:
                    return new LambdaWriteJob(req.Route, () => RouteRequest(req.Route, req.Method, req.Body, req.Format, req.Detail, req.Limit, req.Offset, req.FilterName, req.FilterX, req.FilterY, req.FilterRadius));
            }
        }

        private void FailOutstanding(string error)
        {
            var payload = _jw.Error(error.Replace("\"", "'"));
            while (_pending.TryDequeue(out var req))
                RespondAsync(req.Context, 500, payload);
            while (_writeQueue.Count > 0)
                RespondAsync(_writeQueue.Dequeue().Context, 500, payload);
            if (_activeWriteJob != null && _activeWriteRequest != null)
            {
                _activeWriteJob.Cancel(error);
                RespondAsync(_activeWriteRequest.Context, _activeWriteJob.StatusCode, _activeWriteJob.Result);
                _activeWriteJob = null;
                _activeWriteRequest = null;
            }
        }

        private void RespondAsync(HttpListenerContext ctx, int statusCode, object data)
        {
            ThreadPool.QueueUserWorkItem(_ => Respond(ctx, statusCode, data));
        }

        // Send a JSON response. If data is already a string (from JW serialization),
        // use it directly. Otherwise serialize via Newtonsoft.Json (for anonymous objects).
        // StreamWriter writes directly to the output stream -- no intermediate byte[] allocation.
        private void Respond(HttpListenerContext ctx, int statusCode, object data)
        {
            try
            {
                // JW endpoints return pre-serialized strings; anonymous objects need serialization
                var json = data is string s ? s : JsonConvert.SerializeObject(data);
                ctx.Response.StatusCode = statusCode;
                ctx.Response.ContentType = "application/json";
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                // write directly to output stream -- avoids intermediate byte[] allocation
                // UTF8Encoding(false) = no BOM prefix (JSON parsers reject BOM)
                using (var sw = new StreamWriter(ctx.Response.OutputStream, new UTF8Encoding(false)))
                    sw.Write(json);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                TimberbotLog.Error("response", ex);
            }
        }
    }
}
