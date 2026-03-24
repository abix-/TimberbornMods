# Backlog

Prioritized technical debt and improvements. Ordered by criticality.

## Now (before next release)

| # | Issue | Effort | Details |
|---|---|---|---|
| 1 | Dead StringContent alloc in PushEvent (line 159) | 1 min | Created then never used. Pure GC waste on every event fire |
| 2 | Data payload alloc before webhook count check | 30 min | 68 `new { id, name }` anonymous objects per event batch with 0 subscribers. Move alloc inside PushEvent or check count in handler |
| 3 | `_webhooks.ToArray()` per event fire | 5 min | Allocates array every event. Replace with index loop (main thread, safe) |

## Soon (next release cycle)

| # | Issue | Effort | Details |
|---|---|---|---|
| 4 | 21 silent `catch { }` blocks | 1 hr | Silent data loss, invisible failures if game updates break component access. Log first occurrence per site |
| 5 | No webhook error logging | 15 min | Can't troubleshoot "my webhook doesn't work." Log first failure per URL to Unity console |
| 6 | CachedBuilding 48-field struct copy | 1 hr | 24K field copies per refresh at 1500 buildings. Convert to class (ref copy instead of value copy) |

## Later (quality of life)

| # | Issue | Effort | Details |
|---|---|---|---|
| 7 | Webhook rate limiting | 2 hr | ThreadPool exhaustion if user subscribes to all events. Batch per 200ms or per-type throttle |
| 8 | Webhook circuit breaker | 30 min | Dead URL burns 5s ThreadPool thread per event. After 5 failures, disable webhook + log |
| 9 | Gatherables still uses Dictionary | 15 min | 6.7ms vs ~2ms with StringBuilder. Inconsistent with trees/buildings/beavers |
| 10 | TimberbotService.cs 3500 lines | 3 hr | God object. Extract WebhookService, CacheService, DebugService |
