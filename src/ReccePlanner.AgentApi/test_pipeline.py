"""
End-to-end smoke test for the local pipeline.
Requires both services running:
  - MCP server:  dotnet run --project ../ReccePlanner.McpServer  (port 5000)
  - Agent API:   uvicorn main:app --reload                       (port 8000)

Usage:
  python test_pipeline.py
"""

import asyncio
import httpx
import json
import sys

AGENT_URL = "http://localhost:8000"
MCP_URL   = "http://localhost:5000"

# 100AW 2026 markdown --used directly to test the MCP tools without a PDF upload
AW100_MARKDOWN = """# 2026 100 Acre Wood Rally

## Config

| Parameter                | Value |
|--------------------------|-------|
| Stage recce speed pass 1 | 25    |
| Stage recce speed pass 2 | 30    |

## Stages

| Code | Name             | Distance (mi) | Open time | Close time |
|------|------------------|---------------|-----------|------------|
| 1    | Hazel Creek      | 9.03          | 7:00 am   | 7:00 pm    |
| 2    | Floyd Tower W    | 7.49          | 7:00 am   | 7:00 pm    |
| 5    | KP to Ollie Long | 12.53         | 7:00 am   | 7:00 pm    |
| 6    | Deep Ford        | 4.48          | 7:00 am   | 7:00 pm    |
| 7    | Loop Southern    | 11.03         | 7:00 am   | 7:00 pm    |
| 8    | Nova Scotia S    | 11.4          | 7:00 am   | 7:00 pm    |
| 10   | Crooked Truck S  | 9.18          | 7:00 am   | 7:00 pm    |

## Travel Times (minutes)
Rows = From (Finish), Columns = To (Start)

|    |  1 |  2 |  5 |  6 |  7 |  8 | 10 |
|----|----|----|----|----|----|----|-----|
| 1  | 17 | 44 | 31 | 53 | 73 | 46 | 57 |
| 2  | 28 | 24 | 48 | 67 | 95 | 70 | 74 |
| 5  | 29 | 56 | 12 | 35 | 60 | 51 | 38 |
| 6  | 65 | 85 | 33 | 13 | 33 | 33 | 46 |
| 7  | 64 | 92 | 61 | 49 | 46 | 17 | 73 |
| 8  | 65 | 95 | 45 | 29 |  9 | 25 | 57 |
| 10 | 50 | 79 | 36 | 29 | 42 | 19 | 48 |
"""


def ok(label: str):
    print(f"  [OK] {label}")

def fail(label: str, detail: str = ""):
    print(f"  [FAIL] {label}" + (f": {detail}" if detail else ""))
    sys.exit(1)


async def test_mcp_tools():
    """Test each MCP tool directly against the MCP server."""
    print("\n-- MCP server tools ---------------------------------")

    timeout = httpx.Timeout(connect=3.0, read=60.0, write=10.0, pool=5.0)
    async with httpx.AsyncClient(timeout=timeout) as client:

        # Health / reachability
        try:
            r = await client.get(f"{MCP_URL}/health")
            r.raise_for_status()
            ok("MCP server reachable")
        except Exception as e:
            fail("MCP server reachable", str(e))

        # Use the MCP Python SDK client -- no raw HTTP, no SSE parsing by hand.
        from mcp import ClientSession
        from mcp.client.streamable_http import streamablehttp_client

        async def call_tool(name, args):
            async with streamablehttp_client(MCP_URL) as (read, write, _):
                async with ClientSession(read, write) as session:
                    await session.initialize()
                    return await session.call_tool(name, args)

        def text_of(result) -> str:
            for block in result.content:
                if block.type == "text":
                    return block.text
            raise ValueError(f"No text block in result: {result}")

        # 1. parse_rally_markdown
        result = await call_tool("parse_rally_markdown", {"markdown": AW100_MARKDOWN})
        rally_data = json.loads(text_of(result))
        assert len(rally_data["stages"]) == 7, f"Expected 7 stages, got {len(rally_data['stages'])}"
        assert len(rally_data["travel_times"]) == 49, f"Expected 49 travel time entries, got {len(rally_data['travel_times'])}"
        ok(f"parse_rally_markdown -- {len(rally_data['stages'])} stages, {len(rally_data['travel_times'])} travel times")

        # 2. validate_travel_times
        result = await call_tool("validate_travel_times", {"rallyData": rally_data})
        validation = json.loads(text_of(result))
        assert validation["is_valid"], f"Validation failed: {validation['missing_pairs']}"
        ok(f"validate_travel_times -- valid, {len(validation.get('asymmetric_pairs', []))} asymmetric pairs")

        # 3. optimize_recce (all 7 stages, no time windows -- unconstrained)
        result = await call_tool("optimize_recce", {"rallyData": rally_data})
        opt = json.loads(text_of(result))
        assert opt["feasible"], "Optimizer returned no feasible routes"
        assert opt["optimal_time_minutes"] > 0
        best_route = opt["routes"][0].split("-")
        ok(f"optimize_recce -- {opt['route_count']} optimal route(s), {opt['optimal_time_minutes']} min transit, route: {opt['routes'][0]}")

        # 4. generate_recce_plan
        result = await call_tool("generate_recce_plan", {
            "rallyData": rally_data,
            "route": best_route,
            "label": "100AW 2026 Smoke Test"
        })
        plan_text = text_of(result)
        assert "Recce Plan" in plan_text, "Plan output missing expected header"
        assert "Transit" in plan_text, "Plan output missing transit rows"
        ok(f"generate_recce_plan --{len(plan_text.splitlines())} lines of plan markdown")

        print(f"\n  Plan preview:\n")
        for line in plan_text.splitlines()[:8]:
            print(f"    {line}")
        print("    ...")

        # 5. Verify build_markdown_from_extraction round-trips through parse_rally_markdown
        #    This is the path taken after a PDF upload — the markdown must be parseable.
        from pdf_extractor import build_markdown_from_extraction
        round_trip_md = build_markdown_from_extraction(rally_data)
        assert "## Stages" in round_trip_md, "build_markdown_from_extraction missing Stages section"
        result = await call_tool("parse_rally_markdown", {"markdown": round_trip_md})
        rt_data = json.loads(text_of(result))
        assert len(rt_data["stages"]) == len(rally_data["stages"]), \
            f"Round-trip stage count mismatch: {len(rt_data['stages'])} vs {len(rally_data['stages'])}"
        ok(f"build_markdown_from_extraction -> parse_rally_markdown round-trip -- {len(rt_data['stages'])} stages")

        # 6. PDF extraction ground truth (Oregon Trail 2025 — 19 stages, no travel times)
        #    Skip gracefully if the PDF isn't on this machine.
        import os
        oregon_trail_pdf = (
            r"C:\Users\ygles\OneDrive\Documents\_Rally\2025"
            r"\2-Oregon Trail Rally - May 16-18 2025"
            r"\2025 Oregon Trail Rally Supplementary Regulations.pdf"
        )
        if os.path.exists(oregon_trail_pdf):
            import anthropic as _anthropic
            _key = os.getenv("ANTHROPIC_API_KEY")
            if _key:
                _cl = _anthropic.AsyncAnthropic(api_key=_key)
                from pdf_extractor import extract_from_pdf
                with open(oregon_trail_pdf, "rb") as f:
                    pdf_bytes = f.read()
                extracted = await extract_from_pdf(pdf_bytes, _cl)
                await _cl.close()
                assert len(extracted.get("stages", [])) == 19, \
                    f"Oregon Trail: expected 19 stages, got {len(extracted.get('stages', []))}"
                assert not extracted.get("travel_times"), \
                    "Oregon Trail: expected no travel times, but some were found"
                ok("PDF extraction ground truth -- Oregon Trail 2025: 19 stages, no travel times")
            else:
                print("  [SKIP] PDF ground truth -- ANTHROPIC_API_KEY not set")
        else:
            print("  [SKIP] PDF ground truth -- Oregon Trail PDF not found on this machine")


async def test_agent_api():
    """Test agent API session creation and a basic message exchange."""
    print("\n-- Agent API ----------------------------------------")

    # Use an explicit connect timeout so we fail fast if the service isn't running
    timeout = httpx.Timeout(connect=3.0, read=120.0, write=10.0, pool=5.0)

    async with httpx.AsyncClient(timeout=timeout) as client:

        # Health --skip remaining agent tests gracefully if not running
        try:
            r = await client.get(f"{AGENT_URL}/health")
            r.raise_for_status()
            ok("Agent API reachable")
        except Exception as e:
            print(f"  [WARN] Agent API not running ({e}) -- skipping agent tests.")
            print("     Start it with: uvicorn main:app --reload  (from src/ReccePlanner.AgentApi)")
            return

        # Create session
        r = await client.post(f"{AGENT_URL}/sessions")
        r.raise_for_status()
        session_id = r.json()["session_id"]
        ok(f"Session created: {session_id[:8]}...")

        # Send a message and collect the SSE stream
        user_msg = (
            "Here is the 100AW 2026 rally data in ReccePlanner markdown format. "
            "Please parse it and find the optimal single-day recce route at 25 mph.\n\n"
            f"```\n{AW100_MARKDOWN}\n```"
        )

        chunks = []
        tool_events = []
        async with client.stream(
            "POST",
            f"{AGENT_URL}/sessions/{session_id}/messages",
            json={"content": user_msg},
            timeout=120
        ) as stream:
            async for line in stream.aiter_lines():
                if not line.startswith("data: "):
                    continue
                payload = json.loads(line[6:])
                if payload["type"] == "text":
                    chunks.append(payload["content"])
                elif payload["type"] in ("tool_start", "tool_end", "tool_running"):
                    tool_events.append(payload)
                elif payload["type"] == "done":
                    break

        full_response = "".join(chunks)
        tools_used = [e["name"] for e in tool_events if e["type"] == "tool_start"]

        assert len(full_response) > 50, "Agent response was empty or too short"
        ok(f"Agent responded --{len(full_response)} chars, tools used: {tools_used or '(none yet)'}")

        if tools_used:
            assert "parse_rally_markdown" in tools_used or "optimize_recce" in tools_used, \
                f"Expected optimizer tools to be called, got: {tools_used}"
            ok("Optimizer tools called correctly")

        # Second scenario: no travel times — agent must ask rather than attempt optimization
        r2 = await client.post(f"{AGENT_URL}/sessions")
        r2.raise_for_status()
        sid2 = r2.json()["session_id"]

        no_tt_markdown = AW100_MARKDOWN.split("## Travel Times")[0].rstrip()  # strip TT section
        no_tt_msg = (
            "Here is my rally data. Please parse it and start planning the recce.\n\n"
            f"```\n{no_tt_markdown}\n```"
        )
        chunks2, tools2 = [], []
        async with client.stream(
            "POST", f"{AGENT_URL}/sessions/{sid2}/messages",
            json={"content": no_tt_msg}, timeout=120
        ) as stream:
            async for line in stream.aiter_lines():
                if not line.startswith("data: "): continue
                p = json.loads(line[6:])
                if p["type"] == "text": chunks2.append(p["content"])
                elif p["type"] == "tool_start": tools2.append(p["name"])
                elif p["type"] == "done": break

        response2 = "".join(chunks2).lower()
        assert "optimize_recce" not in tools2, \
            "Agent ran optimizer despite missing travel times"
        assert any(w in response2 for w in ["travel time", "travel_time", "matrix", "minutes"]), \
            "Agent did not ask for travel times when matrix was missing"
        ok("Missing travel times: agent asks for them, does not attempt optimization")


async def main():
    print("ReccePlanner --end-to-end pipeline smoke test")
    print("=" * 52)

    await test_mcp_tools()
    await test_agent_api()

    print("\n" + "=" * 52)
    print("MCP server checks complete. Run agent API to test full pipeline.")


if __name__ == "__main__":
    asyncio.run(main())
