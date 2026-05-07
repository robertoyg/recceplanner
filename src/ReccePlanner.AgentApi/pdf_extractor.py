"""
PDF → structured stage data using Claude vision.
Converts each PDF page to an image and sends all pages to Claude with a
structured extraction prompt. Uses tool_use to force JSON output.
"""

import anthropic
import base64
import io
import os

import fitz  # PyMuPDF — no external dependencies, works on Windows/Linux/macOS

EXTRACTION_MODEL = "claude-opus-4-6"

EXTRACTION_TOOL = {
    "name": "record_extracted_rally_data",
    "description": "Record the stage data extracted from the supplemental regulations PDF.",
    "input_schema": {
        "type": "object",
        "properties": {
            "rally_name": {
                "type": "string",
                "description": "Full rally name as it appears in the document"
            },
            "stages": {
                "type": "array",
                "description": "All rally stages found in the document",
                "items": {
                    "type": "object",
                    "properties": {
                        "code": {"type": "string", "description": "Stage number or code, e.g. '1', 'SS1', 'A'"},
                        "name": {"type": "string", "description": "Stage name"},
                        "distance_miles": {"type": "number", "description": "Stage distance in miles"},
                        "open_time": {"type": "string", "description": "Stage open time in 24h HH:mm format, e.g. '10:00'. Null if not specified."},
                        "close_time": {"type": "string", "description": "Stage close time in 24h HH:mm format, e.g. '20:00'. Null if not specified."}
                    },
                    "required": ["code", "name", "distance_miles"]
                }
            },
            "travel_times": {
                "type": "array",
                "description": "Travel times between stages if a matrix is provided in the document. Leave empty if not present.",
                "items": {
                    "type": "object",
                    "properties": {
                        "from_code": {"type": "string"},
                        "to_code": {"type": "string"},
                        "minutes": {"type": "integer"}
                    },
                    "required": ["from_code", "to_code", "minutes"]
                }
            },
            "extraction_notes": {
                "type": "string",
                "description": "Any ambiguities, assumptions, or missing data noticed during extraction."
            }
        },
        "required": ["rally_name", "stages"]
    }
}

EXTRACTION_PROMPT = """You are extracting rally stage information from supplemental regulations.

Carefully read all pages and extract:
1. The rally name
2. Every stage: code/number, name, distance in miles, open time, close time
3. Travel time matrix between stages (if provided)

Important notes:
- Stage codes are usually short numbers or letters (SS1, 1, A, etc.)
- Distances may be in km — convert to miles (1 km = 0.621371 miles) and note the conversion
- Times are often in 12h format — convert to 24h for the output
- If a travel time matrix is NOT present in the document, leave travel_times empty
- Record any ambiguities or data you're uncertain about in extraction_notes

Use the record_extracted_rally_data tool to return the structured data."""


async def extract_from_pdf(pdf_bytes: bytes, client: anthropic.AsyncAnthropic) -> dict:
    """
    Convert PDF pages to images, send to Claude vision, return structured stage data.
    Returns a dict matching the RallyData schema expected by the MCP server.
    """
    # Convert PDF pages to JPEG images using PyMuPDF (no external dependencies)
    doc = fitz.open(stream=pdf_bytes, filetype="pdf")
    page_images: list[bytes] = []
    for page in doc:
        pix = page.get_pixmap(dpi=150)
        page_images.append(pix.tobytes("jpeg"))
    doc.close()

    # Build content blocks — text prompt + one image per page
    content: list[dict] = [{"type": "text", "text": EXTRACTION_PROMPT}]

    for i, jpeg_bytes in enumerate(page_images):
        b64 = base64.standard_b64encode(jpeg_bytes).decode("utf-8")

        content.append({
            "type": "text",
            "text": f"Page {i + 1} of {len(page_images)}:"
        })
        content.append({
            "type": "image",
            "source": {
                "type": "base64",
                "media_type": "image/jpeg",
                "data": b64
            }
        })

    # Call Claude with forced tool use
    response = await client.messages.create(
        model=EXTRACTION_MODEL,
        max_tokens=4096,
        tools=[EXTRACTION_TOOL],
        tool_choice={"type": "tool", "name": "record_extracted_rally_data"},
        messages=[{"role": "user", "content": content}]
    )

    # Extract the tool call result
    for block in response.content:
        if block.type == "tool_use" and block.name == "record_extracted_rally_data":
            return block.input

    raise RuntimeError("Claude did not return structured extraction data.")


def build_markdown_from_extraction(extracted: dict, pass1_mph: float = 25.0, pass2_mph: float = 25.0) -> str:
    """
    Convert the structured extraction result into the ReccePlanner markdown format.
    This markdown is then passed to parse_rally_markdown on the MCP server.
    """
    rally_name = extracted.get("rally_name", "Rally")
    stages = extracted.get("stages", [])
    travel_times = extracted.get("travel_times", [])

    lines = [f"# {rally_name}", "", "## Config"]
    lines += [
        "| Parameter                | Value |",
        "|--------------------------|-------|",
        f"| Stage recce speed pass 1 | {pass1_mph}    |",
        f"| Stage recce speed pass 2 | {pass2_mph}    |",
        "",
        "## Stages",
        "| Code | Name | Distance (mi) | Open time | Close time |",
        "|------|------|---------------|-----------|------------|"
    ]

    for s in stages:
        code = s.get("code", "")
        name = s.get("name", "")
        dist = s.get("distance_miles", 0)
        open_t = _to_12h(s.get("open_time")) or ""
        close_t = _to_12h(s.get("close_time")) or ""
        lines.append(f"| {code} | {name} | {dist} | {open_t} | {close_t} |")

    if travel_times:
        codes = list(dict.fromkeys(t["from_code"] for t in travel_times))
        header = "| " + " | ".join([""] + codes) + " |"
        separator = "| " + " | ".join(["---"] * (len(codes) + 1)) + " |"
        lines += ["", "## Travel Times (minutes)", header, separator]

        # Build lookup for quick access
        tt_map = {(t["from_code"], t["to_code"]): t["minutes"] for t in travel_times}
        for from_code in codes:
            row_vals = [str(tt_map.get((from_code, to_code), "")) for to_code in codes]
            lines.append("| " + " | ".join([from_code] + row_vals) + " |")

    return "\n".join(lines)


def _to_12h(time_24h: str | None) -> str | None:
    """Convert '13:00' → '1:00 pm', '10:00' → '10:00 am'. Pass-through if already 12h."""
    if not time_24h:
        return None
    try:
        from datetime import datetime
        dt = datetime.strptime(time_24h.strip(), "%H:%M")
        hour = dt.hour % 12 or 12
        ampm = "am" if dt.hour < 12 else "pm"
        return f"{hour}:{dt.minute:02d} {ampm}"  # e.g. "1:00 pm"
    except ValueError:
        return time_24h  # already formatted or unparseable — return as-is

