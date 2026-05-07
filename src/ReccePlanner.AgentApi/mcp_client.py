"""
HTTP client for the ReccePlanner MCP server.
Uses the official MCP Python SDK (streamable-HTTP transport) so SSE framing,
session management, and protocol details are handled by the SDK — not by hand.
"""

import os
import json
from typing import Any

from mcp import ClientSession
from mcp.client.streamable_http import streamablehttp_client

MCP_SERVER_URL = os.getenv("MCP_SERVER_URL", "http://localhost:5000")

# ── Tool definitions for the Anthropic API ────────────────────────────────────
# These mirror the [McpServerTool(Name = "...")] definitions in RecceTools.cs.
# Names are snake_case — matching the server's registered names exactly.

TOOLS: list[dict] = [
    {
        "name": "parse_rally_markdown",
        "description": (
            "Parse a rally markdown document into structured stage data. "
            "Call this first after receiving markdown content from a PDF extraction or user input. "
            "Returns a RallyData object that you pass to all subsequent tools."
        ),
        "input_schema": {
            "type": "object",
            "properties": {
                "markdown": {
                    "type": "string",
                    "description": "Full markdown content in ReccePlanner format (Config, Stages, and Travel Times sections)"
                }
            },
            "required": ["markdown"]
        }
    },
    {
        "name": "validate_travel_times",
        "description": (
            "Validate that the travel time matrix is complete and check for large asymmetries. "
            "Always call this before optimize_recce. "
            "If missing pairs are reported, ask the user to supply them before proceeding."
        ),
        "input_schema": {
            "type": "object",
            "properties": {
                "rallyData": {
                    "type": "object",
                    "description": "Structured rally data returned by parse_rally_markdown"
                },
                "toleranceMinutes": {
                    "type": "integer",
                    "description": "Flag pairs where |A->B - B->A| exceeds this many minutes (default 5)",
                    "default": 5
                }
            },
            "required": ["rallyData"]
        }
    },
    {
        "name": "optimize_recce",
        "description": (
            "Find the optimal recce order for a set of stages using branch-and-bound optimization. "
            "For single-day recce, call once with all stages. "
            "For multi-day analysis, prefer analyze_two_day_split which is more efficient. "
            "Returns all tied-optimal routes and total transit+wait minutes."
        ),
        "input_schema": {
            "type": "object",
            "properties": {
                "rallyData": {
                    "type": "object",
                    "description": "Structured rally data returned by parse_rally_markdown"
                },
                "stageCodes": {
                    "type": "array",
                    "items": {"type": "string"},
                    "description": "Subset of stage codes to optimize. Omit to use all stages."
                },
                "closeTimeOverrides": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "code": {"type": "string"},
                            "time": {"type": "string", "description": "24h HH:mm"}
                        },
                        "required": ["code", "time"]
                    },
                    "description": "Override close times to prune routes finishing after a target time."
                },
                "openTimeOverrides": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "code": {"type": "string"},
                            "time": {"type": "string", "description": "24h HH:mm"}
                        },
                        "required": ["code", "time"]
                    },
                    "description": "Override open times (e.g. to enforce a late start on day 2)."
                },
                "pass1SpeedMph": {
                    "type": "number",
                    "description": "Override pass 1 recce speed in mph."
                },
                "pass2SpeedMph": {
                    "type": "number",
                    "description": "Override pass 2 recce speed in mph."
                }
            },
            "required": ["rallyData"]
        }
    },
    {
        "name": "analyze_two_day_split",
        "description": (
            "Evaluate multiple two-day stage split configurations in one call and return them ranked by total transit time. "
            "Use this instead of calling optimize_recce in a loop. "
            "Results are sorted: feasible splits first (ascending total time), then infeasible."
        ),
        "input_schema": {
            "type": "object",
            "properties": {
                "rallyData": {
                    "type": "object",
                    "description": "Structured rally data returned by parse_rally_markdown"
                },
                "splits": {
                    "type": "array",
                    "description": "Array of split configurations to evaluate",
                    "items": {
                        "type": "object",
                        "properties": {
                            "day1_stage_codes": {
                                "type": "array",
                                "items": {"type": "string"},
                                "description": "Stage codes to recce on day 1"
                            },
                            "day2_stage_codes": {
                                "type": "array",
                                "items": {"type": "string"},
                                "description": "Stage codes to recce on day 2"
                            },
                            "day1_close_time": {
                                "type": "string",
                                "description": "24h HH:mm -- prune routes finishing after this on day 1"
                            },
                            "day2_open_time": {
                                "type": "string",
                                "description": "24h HH:mm -- enforce late start on day 2"
                            },
                            "day2_close_time": {
                                "type": "string",
                                "description": "24h HH:mm -- prune routes finishing after this on day 2"
                            }
                        },
                        "required": ["day1_stage_codes", "day2_stage_codes"]
                    }
                }
            },
            "required": ["rallyData", "splits"]
        }
    },
    {
        "name": "generate_recce_plan",
        "description": (
            "Generate a formatted Markdown recce schedule for a specific route. "
            "Pass one of the route strings returned by optimize_recce or analyze_two_day_split. "
            "Returns the plan as a Markdown string ready to display to the user."
        ),
        "input_schema": {
            "type": "object",
            "properties": {
                "rallyData": {
                    "type": "object",
                    "description": "Structured rally data returned by parse_rally_markdown"
                },
                "route": {
                    "type": "array",
                    "items": {"type": "string"},
                    "description": "Ordered route as stage codes -- each code must appear exactly twice"
                },
                "label": {
                    "type": "string",
                    "description": "Optional day label, e.g. 'Day 1 -- Wednesday April 15'"
                },
                "closeTimeOverrides": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "code": {"type": "string"},
                            "time": {"type": "string"}
                        }
                    }
                },
                "openTimeOverrides": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "code": {"type": "string"},
                            "time": {"type": "string"}
                        }
                    }
                }
            },
            "required": ["rallyData", "route"]
        }
    }
]


async def call_mcp_tool(tool_name: str, tool_input: dict[str, Any]) -> Any:
    """
    Forward a Claude tool call to the MCP server using the official MCP Python SDK.
    The SDK handles SSE framing, session lifecycle, and the initialize handshake.
    tool_name is already snake_case and matches the server's registered name directly.
    """
    async with streamablehttp_client(MCP_SERVER_URL) as (read, write, _):
        async with ClientSession(read, write) as session:
            await session.initialize()
            result = await session.call_tool(tool_name, tool_input)

    # result.content is a list of content blocks; we want the first text block
    for block in result.content:
        if block.type == "text":
            try:
                return json.loads(block.text)
            except json.JSONDecodeError:
                return block.text

    return {}
