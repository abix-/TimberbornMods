// TimberbotService.cs -- Core service: DI constructor, fields, lifecycle, settings.
//
// This is the main entry point for the Timberbot API mod. Timberborn's Bindito DI
// system injects game services into the constructor. The service runs as a game
// singleton: Load() starts the HTTP server, UpdateSingleton() drains queued POST
// requests on the Unity main thread and services fresh snapshot publishes.
//
// API logic lives in separate classes, each with their own DI:
//   TimberbotEntityRegistry -- Entity lookup + tracked refs for writes and v2 snapshots
//   TimberbotWrite         -- All POST write endpoints
//   TimberbotPlacement     -- Building placement, path routing, terrain
//   TimberbotWebhook       -- Batched push event notifications
//   TimberbotDebug         -- Reflection inspector and benchmark

using Timberborn.SingletonSystem;
using UnityEngine;

namespace Timberbot
{
    // HTTP API service. Injected via Bindito DI, runs as game singleton.
    // Returns plain objects serialized to JSON by TimberbotHttpServer.
    //
    // format param: "toon" (default) = flat for tabular display, "json" = full nested data
    // entity access: no typed queries in Timberborn, so we iterate _entityRegistry.Entities + GetComponent<T>()
    // names: CleanName() strips "(Clone)" and the detected faction suffix from all output
    // entity lookup: FindEntity() uses per-frame dictionary cache for O(1) writes
    public class TimberbotService : ILoadableSingleton, IUpdatableSingleton, IUnloadableSingleton
    {
        private readonly EventBus _eventBus;
        public readonly TimberbotEntityRegistry Registry;
        public readonly TimberbotEntityRegistry Cache;
        public readonly TimberbotReadV2 ReadV2;
        public readonly TimberbotWebhook WebhookMgr;
        public readonly TimberbotWrite Write;
        public readonly TimberbotPlacement Placement;
        public readonly TimberbotDebug DebugTool;
        private TimberbotHttpServer _server;

        // settings (loaded from settings.json in mod folder)
        private float _refreshInterval = 1.0f;   // retained for settings compatibility
        private bool _debugEnabled = false;       // enable /api/debug endpoint (default: off)
        private int _httpPort = 8085;             // HTTP server port
        // webhook settings applied to WebhookMgr in Load()
        private bool _webhooksEnabled = true;
        private float _webhookBatchSeconds = 0.2f;
        private int _webhookCircuitBreaker = 30;

        public TimberbotService(
            EventBus eventBus,
            TimberbotEntityRegistry registry,
            TimberbotReadV2 readV2,
            TimberbotWebhook webhookMgr,
            TimberbotWrite write,
            TimberbotPlacement placement,
            TimberbotDebug debug)
        {
            _eventBus = eventBus;
            Registry = registry;
            Cache = registry;
            ReadV2 = readV2;
            WebhookMgr = webhookMgr;
            Write = write;
            Placement = placement;
            DebugTool = debug;
        }

        // Called once when a game is loaded. Starts the HTTP server and hooks into
        // the game's event system.
        //
        // Startup sequence:
        //   1. Load settings.json from mod folder (Documents/Timberborn/Mods/Timberbot/)
        //   2. Initialize logging (fresh log file per session)
        //   3. Wire up cross-references between subsystems (Cache<->Webhooks, Debug<->Service)
        //   4. Register EventBus listeners (entity lifecycle, weather, buildings, etc)
        //   5. Build entity indexes from existing game state (all buildings/beavers/trees)
        //   6. Start HTTP server on configured port
        public void Load()
        {
            LoadSettings();
            var modDir = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "Timberborn", "Mods", "Timberbot");
            TimberbotLog.Init(modDir);
            WebhookMgr.Enabled = _webhooksEnabled;
            WebhookMgr.BatchSeconds = _webhookBatchSeconds;
            WebhookMgr.CircuitBreakerThreshold = _webhookCircuitBreaker;
            TimberbotLog.Info($"v0.7.0 port={_httpPort} refresh={_refreshInterval}s debug={_debugEnabled} webhooks={_webhooksEnabled} batchMs={_webhookBatchSeconds * 1000:F0}");
            Registry.WebhookMgr = WebhookMgr;  // registry pushes webhook events on entity lifecycle
            DebugTool.Service = this;         // debug needs Service reference for endpoint benchmarks
            _eventBus.Register(this);
            WebhookMgr.Register();            // subscribe to 68 game events
            ReadV2.Register();           // subscribe to entity lifecycle events for v2 snapshots
            Registry.Register();              // subscribe to entity lifecycle events
            Placement.DetectFaction();          // detect faction suffix -- must run before BuildAllIndexes
            Registry.BuildAllIndexes();        // populate indexes from existing entities (uses CleanName)
            ReadV2.BuildAll();          // populate v2 building trackers from existing entities
            _server = new TimberbotHttpServer(_httpPort, this, _debugEnabled);
            TimberbotLog.Info($"HTTP server started on port {_httpPort}");
        }

        private void LoadSettings()
        {
            try
            {
                var modDir = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                    "Timberborn", "Mods", "Timberbot");
                var path = System.IO.Path.Combine(modDir, "settings.json");
                if (System.IO.File.Exists(path))
                {
                    var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
                    _refreshInterval = json.Value<float>("refreshIntervalSeconds");
                    if (_refreshInterval <= 0) _refreshInterval = 1.0f;
                    _debugEnabled = json.Value<bool>("debugEndpointEnabled");
                    _httpPort = json.Value<int>("httpPort");
                    if (_httpPort <= 0) _httpPort = 8085;
                    if (json["webhooksEnabled"] != null)
                        _webhooksEnabled = json.Value<bool>("webhooksEnabled");
                    if (json["webhookBatchMs"] != null)
                    {
                        int batchMs = json.Value<int>("webhookBatchMs");
                        _webhookBatchSeconds = batchMs >= 0 ? batchMs / 1000f : 0.2f;
                    }
                    if (json["webhookCircuitBreaker"] != null)
                    {
                        int cb = json.Value<int>("webhookCircuitBreaker");
                        _webhookCircuitBreaker = cb > 0 ? cb : 30;
                    }
                }
            }
            catch (System.Exception ex)
            {
                TimberbotLog.Error("settings.json load failed, using defaults", ex);
            }
        }

        public void Unload()
        {
            ReadV2.Unregister();
            Registry.Unregister();
            WebhookMgr.Unregister();
            _eventBus.Unregister(this);
            _server?.Stop();
            _server = null;
            TimberbotLog.Info("HTTP server stopped");
        }

        // Called every frame by Unity. This is the mod's main loop.
        // It drains POST requests, processes pending fresh-read publishes, and flushes webhooks.
        public void UpdateSingleton()
        {
            float now = Time.realtimeSinceStartup;
            _server?.DrainRequests();
            ReadV2.ProcessPendingRefresh(now);
            WebhookMgr.FlushWebhooks(now);
        }
    }
}
