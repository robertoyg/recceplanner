"""
Core conversation loop.
Manages a Claude conversation with MCP tool use, streaming responses via SSE.
"""

import anthropic
import json
import os
from pathlib import Path
from typing import AsyncGenerator, Callable

from mcp_client import TOOLS, call_mcp_tool

CLAUDE_MODEL = os.getenv("CLAUDE_MODEL", "claude-sonnet-4-6")
MAX_TOKENS = 8192
SYSTEM_PROMPT = (Path(__file__).parent / "system_prompt.md").read_text()


class RecceAgent:
    """
    Stateful agent for a single recce planning session.
    Holds the full conversation history and drives the tool-use loop.
    """

    def __init__(self, client: anthropic.AsyncAnthropic, on_plan_ready: Callable[[str], None] | None = None):
        self._client = client
        self._messages: list[dict] = []
        self._on_plan_ready = on_plan_ready

    def add_user_message(self, text: str) -> None:
        self._messages.append({"role": "user", "content": text})

    def add_user_content(self, content: list[dict]) -> None:
        """Add a user message with mixed content (text + images, or tool results)."""
        self._messages.append({"role": "user", "content": content})

    def ready_to_stream(self) -> bool:
        """True when there is at least one user message waiting for a response."""
        return bool(self._messages) and self._messages[-1]["role"] == "user"

    async def stream_response(self) -> AsyncGenerator[str, None]:
        """
        Drive one full turn: send messages to Claude, handle tool calls,
        and yield SSE-formatted text chunks as they arrive.

        Yields strings of the form:
          data: {"type": "text", "content": "..."}
          data: {"type": "tool_start", "name": "optimize_recce"}
          data: {"type": "tool_end", "name": "optimize_recce"}
          data: {"type": "plan_ready"}
          data: {"type": "done"}
        """
        while True:
            assistant_content: list[dict] = []

            async with self._client.messages.stream(
                model=CLAUDE_MODEL,
                max_tokens=MAX_TOKENS,
                system=SYSTEM_PROMPT,
                tools=TOOLS,
                messages=self._messages
            ) as stream:
                async for event in stream:
                    if event.type == "content_block_start":
                        if event.content_block.type == "tool_use":
                            yield _sse({"type": "tool_start", "name": event.content_block.name})

                    elif event.type == "content_block_delta":
                        if event.delta.type == "text_delta":
                            yield _sse({"type": "text", "content": event.delta.text})

                final_msg = await stream.get_final_message()

            # Reconstruct assistant content blocks for history
            for block in final_msg.content:
                if block.type == "text":
                    assistant_content.append({"type": "text", "text": block.text})
                elif block.type == "tool_use":
                    assistant_content.append({
                        "type": "tool_use",
                        "id": block.id,
                        "name": block.name,
                        "input": block.input
                    })

            self._messages.append({"role": "assistant", "content": assistant_content})

            # If Claude wants to use tools, execute them and loop
            if final_msg.stop_reason == "tool_use":
                tool_results: list[dict] = []

                for block in final_msg.content:
                    if block.type != "tool_use":
                        continue

                    tool_name = block.name
                    tool_input = block.input

                    try:
                        result = await call_mcp_tool(tool_name, tool_input)

                        # Notify when a recce plan is generated
                        if tool_name == "generate_recce_plan" and isinstance(result, str):
                            if self._on_plan_ready:
                                self._on_plan_ready(result)
                            yield _sse({"type": "plan_ready"})

                        result_str = json.dumps(result) if not isinstance(result, str) else result
                        tool_results.append({
                            "type": "tool_result",
                            "tool_use_id": block.id,
                            "content": result_str
                        })
                    except Exception as exc:
                        tool_results.append({
                            "type": "tool_result",
                            "tool_use_id": block.id,
                            "is_error": True,
                            "content": f"Tool error: {exc}"
                        })

                    yield _sse({"type": "tool_end", "name": tool_name})

                # Add tool results as the next user turn and loop
                self._messages.append({"role": "user", "content": tool_results})

            else:
                # stop_reason == "end_turn" — conversation turn complete
                yield _sse({"type": "done"})
                break


def _sse(payload: dict) -> str:
    return f"data: {json.dumps(payload)}\n\n"
