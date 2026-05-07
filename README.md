# ReccePlanner

Finds the optimal scouting (recce) route for a rally crew before a motorsport event. Given a set of stages and travel times between them, it computes the most time-efficient sequence to drive each stage twice — a variant of the Travelling Salesman Problem solved with depth-first search and branch-and-bound pruning.

---

## How it works

Upload your supplemental regulations PDF. A Claude vision model extracts stage data. A Claude agent then drives a C# optimizer via MCP tools to find the optimal route and generate a timed recce schedule.

```
PDF upload
    │
    ▼
Claude vision extraction (PyMuPDF)
    │
    ▼
Claude agent (claude-sonnet-4-6)
    │  tool calls via MCP
    ▼
C# optimizer (DFS + branch-and-bound)
    │
    ▼
Timed recce schedule (Markdown)
```

---

## Architecture

```
src/
  ReccePlanner.Core/        .NET 10 — optimizer engine, parser, models
  ReccePlanner.Console/     .NET 4.8 — standalone CLI (reads markdown input file)
  ReccePlanner.McpServer/   .NET 10 — MCP server exposing optimizer as 5 tools
  ReccePlanner.AgentApi/    Python FastAPI — Claude agent + SSE streaming
  ReccePlanner.Web/         React 18 + Vite — web UI
tests/
  ReccePlannerTests/        NUnit test suite (54 tests)
infra/
  main.bicep                Azure infrastructure (Container Apps, ACR, Static Web App)
```

---

## Local development

### Prerequisites

- .NET 10 SDK
- Python 3.12+
- Node.js 20+
- An [Anthropic API key](https://console.anthropic.com/)

### 1. MCP server

```bash
dotnet run --project src/ReccePlanner.McpServer
# Listening on http://localhost:5000
```

### 2. Agent API

```bash
cd src/ReccePlanner.AgentApi
cp .env.example .env          # fill in ANTHROPIC_API_KEY
pip install -r requirements.txt
uvicorn main:app --reload
# Listening on http://localhost:8000
```

### 3. Web frontend

```bash
cd src/ReccePlanner.Web
npm install
npm run dev
# Open http://localhost:5173
```

### 4. (Optional) Console CLI

```bash
dotnet run --project src/ReccePlanner.Console -- path/to/input.md
```

See `src/ReccePlanner.Console/Input-template.md` for the input format.

---

## Usage

1. Open the web UI and drag-and-drop your rally supplemental regulations PDF
2. The agent extracts stage data using Claude vision and confirms what it found
3. Tell it your recce speed (e.g. 25 mph) and any time constraints
4. It validates the travel time matrix, optimises the route, and generates a timed schedule
5. Download the plan as Markdown

If the PDF doesn't include a travel time matrix between stages, the agent will ask you to provide one before optimising.

---

## MCP tools

The optimizer is exposed as five MCP tools that any MCP-compatible client can call directly:

| Tool | Purpose |
|---|---|
| `parse_rally_markdown` | Parse ReccePlanner markdown → structured stage data |
| `validate_travel_times` | Check travel time matrix for missing or asymmetric pairs |
| `optimize_recce` | Run DFS+B&B optimizer; returns optimal route(s) and transit time |
| `analyze_two_day_split` | Evaluate multiple 2-day stage splits, ranked by total time |
| `generate_recce_plan` | Generate a timed Markdown schedule from a route |

Transport: Streamable HTTP at `http://localhost:5000` (POST with `Accept: application/json, text/event-stream`).

---

## Testing

```bash
# Unit tests (Windows — targets .NET Framework 4.8)
dotnet test tests/ReccePlannerTests/ReccePlannerTests.csproj

# Smoke tests (requires MCP server + agent API running)
cd src/ReccePlanner.AgentApi
python test_pipeline.py

# LLM-as-judge evals (requires live services, costs API credits)
cd src/ReccePlanner.AgentApi/evals
pytest -m eval -v
```

---

## Deployment

Infrastructure is provisioned on Azure (Container Apps + Static Web App) via Bicep.

### One-time setup

1. Create a service principal for GitHub Actions:
   ```bash
   az ad sp create-for-rbac \
     --name "recceplanner-github-actions" \
     --role Contributor \
     --scopes /subscriptions/<subscription-id>/resourceGroups/ReccePlanner \
     --sdk-auth
   ```
   Add the JSON output as GitHub secret `AZURE_CREDENTIALS`.

2. Provision infrastructure:
   ```bash
   az deployment group create \
     --resource-group ReccePlanner \
     --template-file infra/main.bicep \
     --parameters infra/main.bicepparam \
     --parameters anthropicApiKey="$ANTHROPIC_API_KEY"
   ```

3. Get the Static Web App deployment token and add it as GitHub secret `AZURE_STATIC_WEB_APPS_API_TOKEN`:
   ```bash
   az staticwebapp secrets list \
     --name recceplanner-web \
     --resource-group ReccePlanner \
     --query "properties.apiKey" -o tsv
   ```

### Deploy

Push to `main`. The GitHub Actions pipeline runs automatically:

```
test → build + push images to ACR → deploy Container Apps → build + deploy frontend
```

The frontend build automatically injects the live Agent API URL (`VITE_API_URL`) and locks CORS to the Static Web App hostname.

---

## Environment variables

### Agent API (`src/ReccePlanner.AgentApi/.env`)

| Variable | Default | Description |
|---|---|---|
| `ANTHROPIC_API_KEY` | required | Anthropic API key |
| `MCP_SERVER_URL` | `http://localhost:5000` | MCP server URL |
| `CLAUDE_MODEL` | `claude-sonnet-4-6` | Model for agent conversations |
| `CORS_ORIGINS` | `*` | Comma-separated allowed origins |

### Frontend (`src/ReccePlanner.Web`)

| Variable | Description |
|---|---|
| `VITE_API_URL` | Agent API base URL (empty in dev — proxy handles it) |
