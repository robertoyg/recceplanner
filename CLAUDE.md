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

**MCP Tools — registered with snake_case names via `[McpServerTool(Name = "...")]`:**
| Tool (snake_case) | Purpose |
|---|---|
| `parse_rally_markdown` | Parse markdown → structured `RallyData` (always call first) |
| `validate_travel_times` | Check matrix completeness and symmetry (call before optimizing) |
| `optimize_recce` | Run DFS+B&B optimizer; supports stage subset + time overrides |
| `analyze_two_day_split` | Evaluate multiple 2-day splits in one call, ranked by total time |
| `generate_recce_plan` | Generate timed Markdown schedule from a route |

**Critical:** `TimeOverride` fields are `code`/`time` (lowercase) enforced via `[JsonPropertyName]`.

### Agent API (`src/ReccePlanner.AgentApi/` — Python FastAPI)
Claude-powered agent that drives the optimizer via MCP tool calls. Streams responses via SSE.
Run: `uvicorn main:app` from `src/ReccePlanner.AgentApi/` (port 8000). Use `--reload` for dev only.

| File | Role |
|---|---|
| `main.py` | FastAPI app — session management, SSE streaming endpoint, PDF upload |
| `agent.py` | `RecceAgent` — Claude tool-use loop, streams events to client |
| `mcp_client.py` | MCP Python SDK client — `streamablehttp_client` + `ClientSession` |
| `pdf_extractor.py` | Claude vision PDF extraction via PyMuPDF (no external deps) → markdown |
| `system_prompt.md` | Claude system prompt for the recce planning agent |
| `test_pipeline.py` | End-to-end smoke test for MCP tools + agent API |
| `requirements.txt` | Python dependencies (pymupdf, mcp, anthropic, fastapi, pytest…) |
| `Dockerfile` | Production image — no `--reload`, `--workers 1` |
| `evals/judge.py` | LLM-as-judge infrastructure (uses claude-haiku-4-5, returns scored `EvalResult`) |
| `evals/conftest.py` | Shared pytest fixtures for evals |
| `evals/test_agent_behavior.py` | 5 behavioral evals: missing TT, preferences, end-to-end, off-topic, incomplete TT |

**PDF upload flow:**
1. `extract_from_pdf()` — PyMuPDF converts pages to JPEG; Claude vision extracts structured data
2. `build_markdown_from_extraction()` — converts structured dict → ReccePlanner markdown format
3. `_build_upload_message()` — includes the pre-built markdown in the agent's user message so the agent passes it directly to `parse_rally_markdown` (never reconstructs it from the human-readable table)

**Environment variables:**
| Var | Default | Purpose |
|---|---|---|
| `ANTHROPIC_API_KEY` | required | Anthropic API key |
| `MCP_SERVER_URL` | `http://localhost:5000` | MCP server URL |
| `CLAUDE_MODEL` | `claude-sonnet-4-6` | Model for agent turns |
| `CORS_ORIGINS` | `*` | Comma-separated allowed origins (set to SWA hostname in prod) |

### React Frontend (`src/ReccePlanner.Web/` — Vite + React 18)
Run: `npm run dev` (port 5173, proxies `/sessions` and `/health` to port 8000).
Build: `VITE_API_URL=https://... npm run build` — bakes API URL in at build time.

| File | Role |
|---|---|
| `src/App.jsx` | Full app — UploadZone, FileBar, Message components, SSE streaming |
| `src/index.css` | Teal accent theme, markdown table styles, spinner animation |
| `public/logo.png` | Pura Vida Rally Team logo (shown in header) |
| `vite.config.js` | Dev proxy for `/sessions` and `/health` |

**Drag-and-drop notes:** Uses `dragEnter`/`dragLeave` counter (not raw events) to avoid child-element flicker. Global `dragover`/`drop` prevention stops browser navigating to dropped files. Shows immediate extraction feedback message (Claude vision takes ~30-60s).

### Test project (`tests/ReccePlannerTests/` — net48 targeting .NET Framework 4.8)
⚠️ Targets .NET Framework 4.8 — runs on Windows only. CI uses `windows-latest` runner.
| File | Role |
|---|---|
| `RallyParserTests.cs` | Tests for Markdown parsing logic |
| `RallyTests.cs` | Tests for optimization algorithm, plan generation, and two-day analysis |

**Running tests:** `dotnet test tests/ReccePlannerTests/ReccePlannerTests.csproj`
**Running smoke tests:** `python test_pipeline.py` from `src/ReccePlanner.AgentApi/` (requires live services)
**Running evals:** `pytest -m eval -v` from `src/ReccePlanner.AgentApi/evals/` (requires live services, costs API credits)

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

## Azure Deployment

Infrastructure provisioned via `infra/main.bicep` into resource group `ReccePlanner` (West US 2, subscription `9b95613f-a4d0-4cab-93ac-5004db7186d2`).

| Resource | Name | Notes |
|---|---|---|
| Container Registry | `recceplanneracr` | `recceplanneracr.azurecr.io` |
| Container Apps Env | `recceplanner-env` | Linked to Log Analytics `recceplanner-logs` |
| MCP Container App | `recceplanner-mcp` | Internal ingress only (port 5000), 0.5 CPU / 1Gi |
| Agent Container App | `recceplanner-agent` | External ingress (port 8000), 1 CPU / 2Gi |
| Static Web App | `recceplanner-web` | Free tier; CI deploys pre-built `dist/` |

**Live URLs (post first deploy):**
- Frontend: `https://jolly-bush-07122fe1e.7.azurestaticapps.net`
- Agent API: `https://recceplanner-agent.livelyrock-e141f153.westus2.azurecontainerapps.io`

**Re-deploying infrastructure:**
```bash
az deployment group create \
  --resource-group ReccePlanner \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam \
  --parameters anthropicApiKey="$ANTHROPIC_API_KEY"
```
⚠️ `anthropicApiKey` is a `@secure()` parameter — never commit a real value to `main.bicepparam`.

**GitHub Actions secrets required (all 4 must be set):**
- `AZURE_CLIENT_ID` — service principal appId: `az ad sp list --display-name "recceplanner-github" --query "[0].appId" -o tsv`
- `AZURE_CLIENT_SECRET` — service principal password: `az ad app credential reset --id <appId> --query "password" -o tsv`
- `AZURE_TENANT_ID` — `az account show --query "tenantId" -o tsv`
- `AZURE_STATIC_WEB_APPS_API_TOKEN` — `az staticwebapp secrets list --name recceplanner-web --resource-group ReccePlanner --query "properties.apiKey" -o tsv`

⚠️ Do NOT use `azure/login@v1` or `@v2` with a JSON blob — `--sdk-auth` is deprecated and the action's JSON parsing is unreliable. The workflow uses `az login --service-principal` directly with individual secrets.

**CI/CD pipeline (`.github/workflows/deploy.yml`):**
Push to `main` → test (windows-latest) → build+push Docker images → `az containerapp registry set` + update image → `az containerapp ingress update --target-port` → build React with `VITE_API_URL` → deploy SWA (`app_location: src/ReccePlanner.Web/dist`) → lock CORS to SWA hostname.

**Critical first-deploy gotchas (already resolved, document for future):**
- Bicep deploys placeholder `mcr.microsoft.com/azuredocs/containerapps-helloworld:latest` images which listen on port 80; the real apps need ports 5000/8000. Bicep now sets correct ports, and the deploy workflow runs `az containerapp ingress update` on every deploy.
- `anthropic-key` secret must be mapped to `ANTHROPIC_API_KEY` env var explicitly — storing the secret alone is not enough. Set via: `az containerapp update --set-env-vars "ANTHROPIC_API_KEY=secretref:anthropic-key"`
- `az containerapp registry set` must be called before the first `az containerapp update` to wire ACR credentials.
- SWA deploy action: use `app_location: src/ReccePlanner.Web/dist` (not `app_location: src/ReccePlanner.Web` + `output_location: dist`) — the latter deploys source files instead of built output.

---

## Known Limitations / Notes

- Visit count of 2 per stage is hardcoded (not configurable).
- `House` location and `HouseTravelTimes` are defined but currently unused.
- `[InternalsVisibleTo("ReccePlannerTests")]` exposes internals for testing.
- `WaitForInput` property lets tests skip console pauses.
- Agent API sessions are in-memory — a replica restart loses active sessions. Acceptable for single-replica; replace with Redis for multi-replica scaling.
- Bicep deploys placeholder images on first run; GitHub Actions replaces them on first push to `main`.
- `CORS_ORIGINS` is set to `*` by Bicep; the deploy pipeline locks it to the SWA hostname after each frontend deploy.
