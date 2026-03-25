# Backlog

Prioritized technical debt and improvements. Ordered by criticality.

## Now (before next release)

All resolved.

## Resolved

| # | Issue | Fix |
|---|---|---|
| ~~1~~ | Dead StringContent alloc | deleted dead line |
| ~~2~~ | Data payload alloc before count check | guarded with `if (_webhooks.Count > 0)` |
| ~~3~~ | `_webhooks.ToArray()` per event | replaced with index loop |
| ~~4~~ | Silent catches in Cache + Webhooks | `TimberbotLog.Error(context, ex)` -- file + console logging |
| ~~4b~~ | Silent catches in Write, Placement, Debug | same `TimberbotLog.Error` pattern, all 22 catch sites covered |
| ~~5~~ | No error logging | `TimberbotLog` class -- file-based, timestamped, fresh per session |
| ~~6~~ | CachedBuilding 48-field struct copy | converted to class + `Clone()` via MemberwiseClone. Zero value copies |

## Soon (next release cycle)

None pending.

## Later (quality of life)

| # | Issue | Effort | Details |
|---|---|---|---|
| 7 | Webhook rate limiting | 2 hr | ThreadPool exhaustion if user subscribes to all events. Batch per 200ms or per-type throttle |
| 8 | Webhook circuit breaker | 30 min | Dead URL burns 5s ThreadPool thread per event. After 5 failures, disable webhook + log |
| 9 | Gatherables still uses Dictionary | 15 min | 6.7ms vs ~2ms with StringBuilder. Inconsistent with trees/buildings/beavers |
| 10 | TimberbotService.cs 3500 lines | 3 hr | God object. Extract WebhookService, CacheService, DebugService |
