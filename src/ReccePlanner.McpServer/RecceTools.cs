using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using ReccePlanner;

namespace ReccePlanner.McpServer;

[McpServerToolType]
public class RecceTools
{
    private const int OptimizerTimeoutSeconds = 30;

    // ── Tool 1: Parse ─────────────────────────────────────────────────────────

    [McpServerTool(Name = "parse_rally_markdown")]
    [Description(
        "Parse a rally markdown document into structured stage data. " +
        "Call this first after receiving a markdown file or after constructing one from PDF-extracted data. " +
        "Returns a RallyData object that you pass to all subsequent tools.")]
    public RallyData ParseRallyMarkdown(
        [Description("Full markdown content in ReccePlanner format (Config, Stages, and Travel Times sections)")] string markdown)
    {
        var rally = RallyParser.ParseFromString(markdown);
        return RallyData.FromRally(rally);
    }

    // ── Tool 2: Validate ──────────────────────────────────────────────────────

    [McpServerTool(Name = "validate_travel_times")]
    [Description(
        "Validate that the travel time matrix is complete and check for large asymmetries. " +
        "Always call this before optimize_recce. If missing pairs are reported, ask the user to supply them before proceeding.")]
    public ValidationResult ValidateTravelTimes(
        [Description("Structured rally data returned by parse_rally_markdown")] RallyData rallyData,
        [Description("Flag pairs where |A→B − B→A| exceeds this many minutes (default 5)")] int toleranceMinutes = 5)
    {
        var result = new ValidationResult();
        var codes = rallyData.Stages.Select(s => s.Code).ToList();

        // Check completeness: every ordered pair (A→B, B→A for A≠B) should exist
        var ttLookup = rallyData.TravelTimes
            .ToDictionary(t => (t.FromCode, t.ToCode), t => t.Minutes);

        foreach (var from in codes)
        {
            foreach (var to in codes)
            {
                if (from == to) continue;
                if (!ttLookup.ContainsKey((from, to)))
                    result.MissingPairs.Add($"{from}→{to}");
            }
        }

        // Check symmetry
        foreach (var from in codes)
        {
            foreach (var to in codes)
            {
                if (string.Compare(from, to, StringComparison.Ordinal) >= 0) continue;
                if (!ttLookup.TryGetValue((from, to), out var ab)) continue;
                if (!ttLookup.TryGetValue((to, from), out var ba)) continue;
                if (Math.Abs(ab - ba) > toleranceMinutes)
                    result.AsymmetricPairs.Add($"{from}↔{to}: {from}→{to}={ab}min, {to}→{from}={ba}min");
            }
        }

        result.IsValid = result.MissingPairs.Count == 0;

        if (result.AsymmetricPairs.Count > 0)
            result.Warnings.Add($"{result.AsymmetricPairs.Count} asymmetric pair(s) detected. Verify travel times are correct.");

        return result;
    }

    // ── Tool 3: Optimize ──────────────────────────────────────────────────────

    [McpServerTool(Name = "optimize_recce")]
    [Description(
        "Find the optimal recce order for a set of stages using branch-and-bound optimization. " +
        "For single-day recce, call once with all stages. " +
        "For multi-day analysis, prefer analyze_two_day_split which is more efficient. " +
        "Returns all tied-optimal routes and total transit+wait minutes.")]
    public OptimizationResult OptimizeRecce(
        [Description("Structured rally data returned by parse_rally_markdown")] RallyData rallyData,
        [Description("Subset of stage codes to optimize. Omit or pass null to use all stages.")] string[]? stageCodes = null,
        [Description("Override close times: array of {code, time} where time is 24h 'HH:mm'. Prunes routes finishing after this time.")] TimeOverride[]? closeTimeOverrides = null,
        [Description("Override open times: array of {code, time} where time is 24h 'HH:mm'. Useful to enforce a late start on day 2.")] TimeOverride[]? openTimeOverrides = null,
        [Description("Override pass 1 recce speed in mph. Uses config value if omitted.")] double? pass1SpeedMph = null,
        [Description("Override pass 2 recce speed in mph. Uses config value if omitted.")] double? pass2SpeedMph = null)
    {
        var rally = rallyData.ToRally(stageCodes);

        if (pass1SpeedMph.HasValue) rally.Config.StageRecceSpeedPassOneMph = pass1SpeedMph.Value;
        if (pass2SpeedMph.HasValue) rally.Config.StageRecceSpeedPassTwoMph = pass2SpeedMph.Value;

        ApplyTimeOverrides(rally, closeTimeOverrides, openTimeOverrides);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(OptimizerTimeoutSeconds));
        rally.FindOptimalRecce(cts.Token);

        return new OptimizationResult
        {
            Feasible = rally.OptimalRoutes.Count > 0,
            OptimalTimeMinutes = rally.OptimalRoutes.Count > 0 ? rally.OptimalRouteTime : 0,
            RouteCount = rally.OptimalRoutes.Count,
            Routes = rally.OptimalRoutes.Keys.ToList()
        };
    }

    // ── Tool 4: Two-day split analysis ────────────────────────────────────────

    [McpServerTool(Name = "analyze_two_day_split")]
    [Description(
        "Evaluate multiple two-day stage split configurations in one call and return them ranked by total transit time. " +
        "Use this instead of calling optimize_recce in a loop. " +
        "Each split specifies which stages go on day 1 vs day 2, plus optional time-window constraints per day. " +
        "Results are sorted: feasible splits first (ascending total time), infeasible splits last.")]
    public List<SplitResult> AnalyzeTwoDaySplit(
        [Description("Structured rally data returned by parse_rally_markdown")] RallyData rallyData,
        [Description("Array of split configurations to evaluate")] SplitSpec[] splits)
    {
        var results = new List<SplitResult>();

        foreach (var split in splits)
        {
            var result = new SplitResult
            {
                Day1StageCodes = split.Day1StageCodes,
                Day2StageCodes = split.Day2StageCodes
            };

            // Day 1
            var r1 = rallyData.ToRally(split.Day1StageCodes);
            if (!string.IsNullOrWhiteSpace(split.Day1CloseTime) && TimeSpan.TryParse(split.Day1CloseTime, out var d1Close))
                foreach (var loc in r1.Locations) loc.CloseTime = d1Close;

            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(OptimizerTimeoutSeconds));
            r1.FindOptimalRecce(cts1.Token);

            if (r1.OptimalRoutes.Count == 0)
            {
                result.Feasible = false;
                result.InfeasibilityReason = "Day 1 has no feasible route within the time window.";
                results.Add(result);
                continue;
            }

            // Day 2
            var r2 = rallyData.ToRally(split.Day2StageCodes);
            if (!string.IsNullOrWhiteSpace(split.Day2OpenTime) && TimeSpan.TryParse(split.Day2OpenTime, out var d2Open))
            {
                foreach (var loc in r2.Locations)
                    if (!loc.OpenTime.HasValue || loc.OpenTime.Value < d2Open)
                        loc.OpenTime = d2Open;
            }
            if (!string.IsNullOrWhiteSpace(split.Day2CloseTime) && TimeSpan.TryParse(split.Day2CloseTime, out var d2Close))
                foreach (var loc in r2.Locations) loc.CloseTime = d2Close;

            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(OptimizerTimeoutSeconds));
            r2.FindOptimalRecce(cts2.Token);

            if (r2.OptimalRoutes.Count == 0)
            {
                result.Feasible = false;
                result.InfeasibilityReason = "Day 2 has no feasible route within the time window.";
                results.Add(result);
                continue;
            }

            result.Feasible = true;
            result.Day1TransitMinutes = r1.OptimalRouteTime;
            result.Day2TransitMinutes = r2.OptimalRouteTime;
            result.TotalTransitMinutes = r1.OptimalRouteTime + r2.OptimalRouteTime;
            result.Day1Routes = r1.OptimalRoutes.Keys.ToList();
            result.Day2Routes = r2.OptimalRoutes.Keys.ToList();
            results.Add(result);
        }

        // Sort: feasible first (by total time), then infeasible
        return results
            .OrderBy(r => r.Feasible ? 0 : 1)
            .ThenBy(r => r.TotalTransitMinutes ?? int.MaxValue)
            .ToList();
    }

    // ── Tool 5: Generate plan ─────────────────────────────────────────────────

    [McpServerTool(Name = "generate_recce_plan")]
    [Description(
        "Generate a formatted Markdown recce schedule for a specific route. " +
        "Pass one of the route strings returned by optimize_recce or analyze_two_day_split. " +
        "Returns the plan as a Markdown string ready to display to the user.")]
    public string GenerateReccePlan(
        [Description("Structured rally data returned by parse_rally_markdown")] RallyData rallyData,
        [Description("Ordered route as stage codes, e.g. ['2','3','5','6','2','3','5','6'] — each code must appear exactly twice")] string[] route,
        [Description("Optional day label appended to the plan heading, e.g. 'Day 1 — Wednesday April 15'")] string? label = null,
        [Description("Override close times to apply before generating (useful for day-constrained plans). Array of {code, time} in 24h 'HH:mm'.")] TimeOverride[]? closeTimeOverrides = null,
        [Description("Override open times to apply before generating. Array of {code, time} in 24h 'HH:mm'.")] TimeOverride[]? openTimeOverrides = null)
    {
        // Build rally with only the stages referenced in the route
        var routeCodes = route.Distinct().ToArray();
        var rally = rallyData.ToRally(routeCodes);
        ApplyTimeOverrides(rally, closeTimeOverrides, openTimeOverrides);

        // Map route codes → Location objects
        var locByCode = rally.Locations.ToDictionary(l => l.Code);
        var locationRoute = route
            .Where(c => locByCode.ContainsKey(c))
            .Select(c => locByCode[c])
            .ToList();

        if (!string.IsNullOrWhiteSpace(label))
            rally.Name = $"{rally.Name} — {label}";

        var markdown = rally.GenerateReccePlanMarkdown(locationRoute);
        return markdown ?? "Error: no stages have open times set; cannot generate a timed plan.";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ApplyTimeOverrides(Rally rally, TimeOverride[]? closeOverrides, TimeOverride[]? openOverrides)
    {
        if (closeOverrides != null)
        {
            foreach (var ov in closeOverrides)
            {
                if (!TimeSpan.TryParse(ov.Time, out var ts)) continue;
                var loc = rally.Locations.FirstOrDefault(l => l.Code == ov.Code);
                if (loc != null) loc.CloseTime = ts;
            }
        }

        if (openOverrides != null)
        {
            foreach (var ov in openOverrides)
            {
                if (!TimeSpan.TryParse(ov.Time, out var ts)) continue;
                var loc = rally.Locations.FirstOrDefault(l => l.Code == ov.Code);
                if (loc != null) loc.OpenTime = ts;
            }
        }
    }
}

/// <summary>Used by optimize_recce and generate_recce_plan to override stage time windows.</summary>
public class TimeOverride
{
    [JsonPropertyName("code")]
    [Description("Stage code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("time")]
    [Description("24h time string 'HH:mm', e.g. '18:30'")]
    public string Time { get; set; } = "";
}
