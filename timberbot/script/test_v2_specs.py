from dataclasses import dataclass


@dataclass(frozen=True)
class EndpointSpec:
    name: str
    legacy_path: str
    v2_path: str
    group: str
    supports_format: bool = False
    supports_pagination: bool = False
    supports_name_filter: bool = False
    supports_radius_filter: bool = False
    supports_detail_basic: bool = False
    supports_detail_full: bool = False
    supports_detail_id: bool = False
    projection_backed: bool = False
    compare_mode: str = "exact"


@dataclass(frozen=True)
class FreshnessScenario:
    name: str
    kind: str
    discover_key: str = ""


ENDPOINT_SPECS = [
    EndpointSpec("ping", "/api/ping", "/api/v2/ping", "scalar"),
    EndpointSpec("settlement", "/api/settlement", "/api/v2/settlement", "scalar"),
    EndpointSpec("population", "/api/population", "/api/v2/population", "scalar"),
    EndpointSpec("time", "/api/time", "/api/v2/time", "scalar"),
    EndpointSpec("weather", "/api/weather", "/api/v2/weather", "scalar"),
    EndpointSpec("workhours", "/api/workhours", "/api/v2/workhours", "scalar"),
    EndpointSpec("speed", "/api/speed", "/api/v2/speed", "scalar"),
    EndpointSpec("prefabs", "/api/prefabs", "/api/v2/prefabs", "scalar"),
    EndpointSpec("summary", "/api/summary", "/api/v2/summary", "format", supports_format=True),
    EndpointSpec("resources", "/api/resources", "/api/v2/resources", "format", supports_format=True),
    EndpointSpec("districts", "/api/districts", "/api/v2/districts", "format", supports_format=True),
    EndpointSpec("distribution", "/api/distribution", "/api/v2/distribution", "format", supports_format=True),
    EndpointSpec("science", "/api/science", "/api/v2/science", "format", supports_format=True),
    EndpointSpec("wellbeing", "/api/wellbeing", "/api/v2/wellbeing", "format", supports_format=True),
    EndpointSpec("power", "/api/power", "/api/v2/power", "format", supports_format=True),
    EndpointSpec("tree_clusters", "/api/tree_clusters", "/api/v2/tree_clusters", "format", supports_format=True),
    EndpointSpec("food_clusters", "/api/food_clusters", "/api/v2/food_clusters", "format", supports_format=True),
    EndpointSpec("alerts", "/api/alerts", "/api/v2/alerts", "paged", supports_format=True, supports_pagination=True),
    EndpointSpec("notifications", "/api/notifications", "/api/v2/notifications", "paged", supports_format=True, supports_pagination=True),
    EndpointSpec(
        "buildings", "/api/buildings", "/api/v2/buildings", "detail_list",
        supports_format=True, supports_pagination=True, supports_name_filter=True,
        supports_radius_filter=True, supports_detail_basic=True, supports_detail_full=True,
        supports_detail_id=True, projection_backed=True, compare_mode="list",
    ),
    EndpointSpec(
        "beavers", "/api/beavers", "/api/v2/beavers", "detail_list",
        supports_format=True, supports_pagination=True, supports_name_filter=True,
        supports_radius_filter=True, supports_detail_basic=True, supports_detail_full=True,
        supports_detail_id=True, compare_mode="list",
    ),
    EndpointSpec(
        "trees", "/api/trees", "/api/v2/trees", "list",
        supports_format=True, supports_pagination=True, supports_name_filter=True,
        supports_radius_filter=True, compare_mode="list",
    ),
    EndpointSpec(
        "crops", "/api/crops", "/api/v2/crops", "list",
        supports_format=True, supports_pagination=True, supports_name_filter=True,
        supports_radius_filter=True, compare_mode="list",
    ),
    EndpointSpec(
        "gatherables", "/api/gatherables", "/api/v2/gatherables", "list",
        supports_format=True, supports_pagination=True, supports_name_filter=True,
        supports_radius_filter=True, compare_mode="list",
    ),
]


FRESHNESS_SCENARIOS = [
    FreshnessScenario("pause_toggle", "pause_toggle", "pause_target"),
    FreshnessScenario("workers_change", "workers_change", "workers_target"),
    FreshnessScenario("place_demolish", "place_demolish", "placement_search"),
    FreshnessScenario("floodgate_change", "floodgate_change", "floodgate_target"),
    FreshnessScenario("recipe_change", "recipe_change", "recipe_target"),
    FreshnessScenario("clutch_change", "clutch_change", "clutch_target"),
]


ENDPOINT_MAP = {spec.name: spec for spec in ENDPOINT_SPECS}
GROUP_NAMES = sorted({spec.group for spec in ENDPOINT_SPECS})
