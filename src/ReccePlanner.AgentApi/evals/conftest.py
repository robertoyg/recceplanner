"""
Shared pytest fixtures for ReccePlanner agent evals.
"""

import os
import sys

import anthropic
import pytest
import pytest_asyncio

# Make the parent package importable
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

from dotenv import load_dotenv
load_dotenv(os.path.join(os.path.dirname(os.path.dirname(__file__)), ".env"))

AGENT_URL = os.getenv("AGENT_URL", "http://localhost:8000")
MCP_URL   = os.getenv("MCP_SERVER_URL", "http://localhost:5000")


# ── Fixtures ──────────────────────────────────────────────────────────────────

@pytest_asyncio.fixture
async def anthropic_client():
    client = anthropic.AsyncAnthropic(api_key=os.environ["ANTHROPIC_API_KEY"])
    yield client
    await client.close()


@pytest_asyncio.fixture
async def http_client():
    import httpx
    async with httpx.AsyncClient(timeout=120) as client:
        yield client


@pytest_asyncio.fixture
async def agent_session(http_client):
    """Create a fresh agent session and return its ID."""
    r = await http_client.post(f"{AGENT_URL}/sessions")
    r.raise_for_status()
    return r.json()["session_id"]


# ── Helpers ───────────────────────────────────────────────────────────────────

async def stream_agent(http_client, session_id: str, content: str) -> tuple[str, list[str]]:
    """
    Send a message to the agent and collect the full text response + list of
    tool names that were called. Returns (full_text, tools_called).
    """
    import json
    chunks, tools = [], []
    async with http_client.stream(
        "POST",
        f"{AGENT_URL}/sessions/{session_id}/messages",
        json={"content": content},
        timeout=120,
    ) as stream:
        async for line in stream.aiter_lines():
            if not line.startswith("data: "):
                continue
            ev = json.loads(line[6:])
            if ev["type"] == "text":
                chunks.append(ev["content"])
            elif ev["type"] == "tool_start":
                tools.append(ev["name"])
            elif ev["type"] == "done":
                break
    return "".join(chunks), tools


# ── Markers ───────────────────────────────────────────────────────────────────

def pytest_configure(config):
    config.addinivalue_line(
        "markers",
        "eval: LLM-as-judge evals (slow, costs API credits — run with: pytest -m eval)"
    )
    config.addinivalue_line(
        "markers",
        "requires_services: needs MCP server + agent API running locally"
    )
