using System.ComponentModel;
using System.Text.Json.Serialization;
using ReccePlanner;

namespace ReccePlanner.McpServer;

// ── Input / transfer model ────────────────────────────────────────────────────

public class RallyData
{
    [JsonPropertyName("rally_name")]
    public string RallyName { get; set; } = "Rally";

    [JsonPropertyName("config")]
    public ConfigData Config { get; set; } = new();

    [JsonPropertyName("stages")]
    public List<StageData> Stages { get; set; } = new();

    [JsonPropertyName("travel_times")]
    public List<TravelTimeData> TravelTimes { get; set; } = new();

    /// <summary>Convert to a Rally domain object, optionally filtering to a subset of stage codes.</summary>
    public Rally ToRally(string[]? stageCodes = null)
    {
        var rally = new Rally
        {
            Name = RallyName,
            Config = new RallyConfig
            {
                StageRecceSpeedPassOneMph = Config.Pass1SpeedMph,
                StageRecceSpeedPassTwoMph = Config.Pass2SpeedMph
            },
            WaitForInput = false,
            SuppressOutput = true
        };

        var locationMap = new Dictionary<string, Location>();

        foreach (var s in Stages)
        {
            if (stageCodes != null && !stageCodes.Contains(s.Code)) continue;

            var loc = new Location(s.Name, s.Code) { DistanceMiles = s.DistanceMiles };
            if (!string.IsNullOrWhiteSpace(s.OpenTime) && TimeSpan.TryParse(s.OpenTime, out var open))
                loc.OpenTime = open;
            if (!string.IsNullOrWhiteSpace(s.CloseTime) && TimeSpan.TryParse(s.CloseTime, out var close))
                loc.CloseTime = close;

            locationMap[s.Code] = loc;
            rally.Locations.Add(loc);
        }

        foreach (var tt in TravelTimes)
        {
            if (!locationMap.TryGetValue(tt.FromCode, out var from)) continue;
            if (!locationMap.TryGetValue(tt.ToCode, out var to)) continue;
            rally.TravelTimes.Add(new Route(from, to, tt.Minutes));
        }

        return rally;
    }

    /// <summary>Build from a parsed Rally domain object.</summary>
    public static RallyData FromRally(Rally rally)
    {
        var data = new RallyData
        {
            RallyName = rally.Name,
            Config = new ConfigData
            {
                Pass1SpeedMph = rally.Config.StageRecceSpeedPassOneMph,
                Pass2SpeedMph = rally.Config.StageRecceSpeedPassTwoMph
            }
        };

        foreach (var loc in rally.Locations)
        {
            data.Stages.Add(new StageData
            {
                Code = loc.Code,
                Name = loc.Name,
                DistanceMiles = loc.DistanceMiles,
                OpenTime = loc.OpenTime.HasValue ? loc.OpenTime.Value.ToString(@"hh\:mm") : null,
                CloseTime = loc.CloseTime.HasValue ? loc.CloseTime.Value.ToString(@"hh\:mm") : null
            });
        }

        foreach (var tt in rally.TravelTimes)
        {
            data.TravelTimes.Add(new TravelTimeData
            {
                FromCode = tt.Source.Code,
                ToCode = tt.Target.Code,
                Minutes = tt.Time
            });
        }

        return data;
    }
}

public class ConfigData
{
    [JsonPropertyName("pass1_speed_mph")]
    public double Pass1SpeedMph { get; set; } = 30;

    [JsonPropertyName("pass2_speed_mph")]
    public double Pass2SpeedMph { get; set; } = 30;
}

public class StageData
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("distance_miles")]
    public double DistanceMiles { get; set; }

    /// <summary>24h time string "HH:mm", e.g. "10:00". Null if no restriction.</summary>
    [JsonPropertyName("open_time")]
    public string? OpenTime { get; set; }

    /// <summary>24h time string "HH:mm", e.g. "20:00". Null if no restriction.</summary>
    [JsonPropertyName("close_time")]
    public string? CloseTime { get; set; }
}

public class TravelTimeData
{
    [JsonPropertyName("from_code")]
    public string FromCode { get; set; } = "";

    [JsonPropertyName("to_code")]
    public string ToCode { get; set; } = "";

    [JsonPropertyName("minutes")]
    public int Minutes { get; set; }
}

// ── Split analysis ────────────────────────────────────────────────────────────

public class SplitSpec
{
    [JsonPropertyName("day1_stage_codes")]
    [Description("Stage codes to recce on day 1")]
    public string[] Day1StageCodes { get; set; } = Array.Empty<string>();

    [JsonPropertyName("day2_stage_codes")]
    [Description("Stage codes to recce on day 2")]
    public string[] Day2StageCodes { get; set; } = Array.Empty<string>();

    /// <summary>24h "HH:mm" — prune routes finishing after this time on day 1. Null = no constraint.</summary>
    [JsonPropertyName("day1_close_time")]
    public string? Day1CloseTime { get; set; }

    /// <summary>24h "HH:mm" — override open times earlier than this on day 2 (enforces late start). Null = no constraint.</summary>
    [JsonPropertyName("day2_open_time")]
    public string? Day2OpenTime { get; set; }

    /// <summary>24h "HH:mm" — prune routes finishing after this time on day 2. Null = no constraint.</summary>
    [JsonPropertyName("day2_close_time")]
    public string? Day2CloseTime { get; set; }
}

public class SplitResult
{
    [JsonPropertyName("day1_stage_codes")]
    public string[] Day1StageCodes { get; set; } = Array.Empty<string>();

    [JsonPropertyName("day2_stage_codes")]
    public string[] Day2StageCodes { get; set; } = Array.Empty<string>();

    [JsonPropertyName("feasible")]
    public bool Feasible { get; set; }

    [JsonPropertyName("day1_transit_minutes")]
    public int? Day1TransitMinutes { get; set; }

    [JsonPropertyName("day2_transit_minutes")]
    public int? Day2TransitMinutes { get; set; }

    [JsonPropertyName("total_transit_minutes")]
    public int? TotalTransitMinutes { get; set; }

    [JsonPropertyName("day1_routes")]
    public List<string>? Day1Routes { get; set; }

    [JsonPropertyName("day2_routes")]
    public List<string>? Day2Routes { get; set; }

    [JsonPropertyName("infeasibility_reason")]
    public string? InfeasibilityReason { get; set; }
}

// ── Optimization result ───────────────────────────────────────────────────────

public class OptimizationResult
{
    [JsonPropertyName("feasible")]
    public bool Feasible { get; set; }

    [JsonPropertyName("optimal_time_minutes")]
    public int OptimalTimeMinutes { get; set; }

    [JsonPropertyName("route_count")]
    public int RouteCount { get; set; }

    /// <summary>Route descriptions like "1-2-3-4-1-2-3-4" (each code appears twice).</summary>
    [JsonPropertyName("routes")]
    public List<string> Routes { get; set; } = new();
}

// ── Validation result ─────────────────────────────────────────────────────────

public class ValidationResult
{
    [JsonPropertyName("is_valid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("missing_pairs")]
    public List<string> MissingPairs { get; set; } = new();

    [JsonPropertyName("asymmetric_pairs")]
    public List<string> AsymmetricPairs { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}
