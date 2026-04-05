# Recce Planner — Claude Code Instructions

> **Keep this file up to date.** Any time logic, architecture, config parameters, file roles, or behavior changes, update the relevant section here before finishing the task.

---

## Testing

**Always validate and update tests when working on this application.**

- Run tests before starting any change to establish a baseline: `dotnet test ReccePlannerTests/ReccePlannerTests.csproj`
- Every new feature, config parameter, model field, or behavior change must have corresponding tests.
- Every bug fix must have a regression test.
- Run tests after every change to confirm nothing is broken: `dotnet test ReccePlannerTests/ReccePlannerTests.csproj`
- Parser changes → add tests in `ReccePlannerTests/RallyParserTests.cs`
- Algorithm or plan generation changes → add tests in `ReccePlannerTests/RallyTests.cs`
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

| File | Role |
|---|---|
| `Program.cs` | Entry point — reads CLI arg, calls parser then optimizer |
| `RallyParser.cs` | Parses Markdown input file into model objects |
| `Rally.cs` | DFS + branch-and-bound optimization engine + plan generation |
| `Location.cs` | Stage model: name, code, distance |
| `Route.cs` | Travel route model: source, target, travel time (minutes) |
| `RallyConfig.cs` | Config: stage recce speeds (pass 1 and pass 2 mph) |
| `Input-template.md` | Sample input (Olympus Rally, 3 stages) |
| `Output-Sample.md` | Sample output (timed recce schedule) |

Test project: `ReccePlannerTests/`
| File | Role |
|---|---|
| `RallyParserTests.cs` | Tests for Markdown parsing logic |
| `RallyTests.cs` | Tests for optimization algorithm and plan generation |

---

## Known Limitations / Notes

- Visit count of 2 per stage is hardcoded (not configurable).
- `House` location and `HouseTravelTimes` are defined but currently unused.
- `[InternalsVisibleTo("ReccePlannerTests")]` exposes internals for testing.
- `WaitForInput` property lets tests skip console pauses.
