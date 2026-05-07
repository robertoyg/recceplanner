# Recce Planner — Claude Code Instructions

> **Keep this file up to date.** Any time logic, architecture, config parameters, file roles, or behavior changes, update the relevant section here before finishing the task.

---

## Testing

**Always validate and update tests when working on this application.**

- Run tests before starting any change to establish a baseline: `dotnet test tests/ReccePlannerTests/ReccePlannerTests.csproj`
- Every new feature, config parameter, model field, or behavior change must have corresponding tests.
- Every bug fix must have a regression test.
- Run tests after every change to confirm nothing is broken: `dotnet test tests/ReccePlannerTests/ReccePlannerTests.csproj`
- Parser changes → add tests in `tests/ReccePlannerTests/RallyParserTests.cs`
- Algorithm or plan generation changes → add tests in `tests/ReccePlannerTests/RallyTests.cs`
- Do not mark a task complete if tests are failing.

---

## How the Application Works

Recce Planner is a C# console app that finds the optimal order for a rally crew to scout (recce) stages before a motorsport event. Given N stages and a travel-time matrix between them, it finds the most time-efficient sequence to visit each stage **exactly twice** (two recce passes). This is a variant of the Traveling Salesman Problem.

### Flow

1. **Input**: A Markdown file is passed as a CLI argument.
2. **Parsing** (`RallyParser.cs`): Reads the Markdown tables — config, stages, and travel times — into model objects.
3. **Optimization** (`Rally.cs`): Runs DFS + branch-and-bound to find the minimum-cost visit sequence.
4. **Output**: Prints a timed recce schedule to the console showing arrival times at each stage for each pass.

### Algorithm (`Rally.cs`)

- Depth-first search with backtracking over all permutations of stage visits.
- **Branch-and-bound pruning**: abandons any partial path where `partialCost + stepCost > optimalRouteTime`.
- **Parallelized at the first level only**: one thread per starting stage using `Parallel.ForEach`.
- Thread safety: `Mutex` on `optimalRoutes`, `volatile` on `optimalRouteTime`.
- O(1) travel time lookup via `_travelTimeMap` dictionary keyed on `(Source, Target)`.
- Visit count of **2 per stage** is hardcoded.
- Each stage has a distance (miles) and a configured speed (mph) to calculate time-on-stage.
- Two speeds are supported: Pass 1 speed and Pass 2 speed (both from config).
- **Recce start time** is derived automatically as the earliest `Open time` among all stages. If no stages have an open time, time-window enforcement is disabled entirely.
- **Open-time enforcement**: arriving before a stage opens adds wait time to the route cost (not a discard). If `partialCost + transit + wait > bestKnown`, the branch is pruned.
- **Close-time enforcement**: finishing after a stage's close time always discards the branch.
- **Stage selection**: `Program.cs` prompts at startup to include all stages or a subset by code.

### Input Format (Markdown)

```markdown
# Rally Name

## Config
| Parameter                | Value |
|--------------------------|-------|
| Stage recce speed pass 1 | 30    |
| Stage recce speed pass 2 | 30    |

## Stages
| Code | Name       | Distance (mi) | Open time | Close time |
|------|------------|---------------|-----------|------------|
| 1    | Stage Name | 6.3           | 11:00 am  | 8:00 pm    |

## Travel Times (minutes)
|   | 1  | 2  |
|---|----|----|
| 1 | 5  | 10 |
| 2 | 10 | 5  |
```

- `Open time` and `Close time` columns in the Stages table are optional per-stage.
- Recce start time = minimum `Open time` across all stages (no config parameter needed).

### Output Format

A timed schedule listing each stage visit in order with:
- Arrival time (and a "Waiting to open" row if arriving before stage open time)
- Stage name and pass number
- Departure time (arrival + wait if any + drive time)

---

## Project Layout

```
src/
  ReccePlanner.Core/        .NET 10 class library — optimizer, parser, models
  ReccePlanner.Console/     .NET 4.8 console app — CLI entry point (links Core sources)
  ReccePlanner.McpServer/   .NET 10 ASP.NET — MCP server exposing optimizer as tools
  ReccePlanner.AgentApi/    Python FastAPI — Claude agent + SSE streaming endpoint
tests/
  ReccePlannerTests/        NUnit test project (.NET 10)
```

### Core library (`src/ReccePlanner.Core/` — net10.0)
| File | Role |
|---|---|
| `Rally.cs` | DFS + branch-and-bound optimization engine + plan generation |
| `RallyParser.cs` | Parses Markdown input into model objects; `ParseFromString` for server use |
| `Location.cs` | Stage model: name, code, distance, open/close times |
| `Route.cs` | Travel route model: source, target, travel time (minutes) |
| `RallyConfig.cs` | Config: stage recce speeds (pass 1 and pass 2 mph) |

Key APIs added for server use:
- `RallyParser.ParseFromString(string content)` — parses markdown from a string (no file I/O)
- `Rally.GenerateReccePlanMarkdown(List<Location> route)` — returns plan as a string
- `Rally.SuppressOutput` — suppresses Console output when `true`
- `Rally.FindOptimalRecce(CancellationToken)` — supports 30s timeout from MCP server

### Console app (`src/ReccePlanner.Console/` — net4.8)
Links Core source files via `<Compile Include="..\ReccePlanner.Core\*.cs"><Link>` pattern.
| File | Role |
|---|---|
| `Program.cs` | Entry point — reads CLI arg, prompts for stage selection, runs optimizer |
| `Input-template.md` | Sample input (Olympus Rally, 3 stages) |
| `Output-Sample.md` | Sample output (timed recce schedule) |

### MCP server (`src/ReccePlanner.McpServer/` — net10.0 ASP.NET)
Exposes the optimizer as an MCP (Model Context Protocol) server for Claude agents.
Uses `ModelContextProtocol.AspNetCore` NuGet package.
Transport: Streamable HTTP at `/` — POST with `Accept: application/json, text/event-stream`, responds with SSE.
Run: `dotnet run --project src/ReccePlanner.McpServer` (port 5000)

| File | Role |
|---|---|
| `Program.cs` | ASP.NET host — registers MCP server + `/health` endpoint |
| `RecceTools.cs` | All 5 MCP tool implementations (PascalCase method names = tool names) |
| `Models.cs` | `RallyData`, `SplitSpec`, `SplitResult`, `OptimizationResult`, `ValidationResult` |
| `Dockerfile` | Multi-stage build for Azure Container Apps |

**MCP Tools (PascalCase as registered by the SDK):**
| Tool | Purpose |
|---|---|
| `ParseRallyMarkdown` | Parse markdown → structured `RallyData` (call first) |
| `ValidateTravelTimes` | Check matrix completeness and symmetry (call before optimizing) |
| `OptimizeRecce` | Run DFS+B&B optimizer; supports stage subset + time overrides |
| `AnalyzeTwoDaySplit` | Evaluate multiple 2-day splits in one call, ranked by total time |
| `GenerateReccePlan` | Generate timed Markdown schedule from a route |

### Agent API (`src/ReccePlanner.AgentApi/` — Python FastAPI)
Claude-powered agent that drives the optimizer via MCP tool calls. Streams responses via SSE.
Run: `uvicorn main:app --reload` from `src/ReccePlanner.AgentApi/` (port 8000)

| File | Role |
|---|---|
| `main.py` | FastAPI app — session management, SSE streaming endpoint, PDF upload |
| `agent.py` | `RecceAgent` — Claude tool-use loop, streams events to client |
| `mcp_client.py` | HTTP client for MCP server — maps snake_case tool names to PascalCase, parses SSE |
| `pdf_extractor.py` | Claude vision PDF extraction → ReccePlanner markdown |
| `system_prompt.md` | Claude system prompt for the recce planning agent |
| `test_pipeline.py` | End-to-end smoke test for MCP tools + agent API |
| `requirements.txt` | Python dependencies |
| `Dockerfile` | Container image for Azure Container Apps |

### Test project (`tests/ReccePlannerTests/` — net10.0)
| File | Role |
|---|---|
| `RallyParserTests.cs` | Tests for Markdown parsing logic |
| `RallyTests.cs` | Tests for optimization algorithm and plan generation |

---

## Two-Day Recce Analysis

When a rally has too many stages to recce in a single day, use the test infrastructure in `RallyTests.cs` to find the optimal stage split across two days. Two analysis methods exist (both marked `[Ignore]` — remove to run manually):

### `Olympus2026_FullAnalysisAndGeneratePlans`
Broad survey of all stage splits, both days treated symmetrically. Use when:
- Both recce days have the same or similar window (e.g., both 10am–8pm)
- No special start/end time requirements per day
- Want to find the globally minimum total transit time

### `Olympus2026_SunsetConstrainedAnalysis` ← **preferred for real events**
Applies per-day time bounds enforced inside the optimizer (via `CloseTime` overrides). Use when:
- Day 1 must finish by a target time (e.g., 6:30pm for shakedown)
- Day 2 has a late start (e.g., 1pm) and must finish by sunset (e.g., 7pm)
- A fixed set of stages is anchored to one day (e.g., specific stages always on Day 2)

**Key pattern** — time bounds are enforced by overriding stage model fields before running:
```csharp
// Day 1: cap close time so optimizer prunes routes that run late
foreach (var loc in r.Locations) loc.CloseTime = new TimeSpan(18, 30, 0);

// Day 2: push open time to enforce late start; cap close for sunset
foreach (var loc in r.Locations) {
    if (loc.OpenTime < day2Open) loc.OpenTime = day2Open;
    loc.CloseTime = new TimeSpan(19, 0, 0);
}
```

**How to adapt for a new rally:**
1. Update the file path and window `TimeSpan` constants
2. Set the "base" stages anchored to each day in the splits array
3. Enumerate how to distribute the remaining stages (typically late-opening ones)
4. Run — feasible splits print transit+wait totals; plan `.md` files are saved to `outputDir`

---

## Known Limitations / Notes

- Visit count of 2 per stage is hardcoded (not configurable).
- `House` location and `HouseTravelTimes` are defined but currently unused.
- `[InternalsVisibleTo("ReccePlannerTests")]` exposes internals for testing.
- `WaitForInput` property lets tests skip console pauses.
