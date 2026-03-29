# Recce Planner — Claude Code Instructions

## Testing

**Always update tests when adding new functionality.**

- Every new feature, config parameter, model field, or behavior change must have corresponding tests.
- Parser changes → add tests in `ReccePlannerTests/RallyParserTests.cs`
- Algorithm or plan generation changes → add tests in `ReccePlannerTests/RallyTests.cs`
- Run tests after every change: `dotnet test ReccePlannerTests/ReccePlannerTests.csproj`

## Project layout

| File | Role |
|---|---|
| `Program.cs` | Entry point |
| `RallyParser.cs` | Parses Markdown input files |
| `Rally.cs` | Optimization engine + plan generation |
| `Location.cs` | Stage model (name, code, distance) |
| `Route.cs` | Travel route model (source, target, time) |
| `RallyConfig.cs` | Config (speed, start time) |
| `Input-template.md` | Sample input (Olympus Rally, 3 stages) |
| `Output-Sample.md` | Sample output (timed recce schedule) |
