"""
FastAPI application — entry point for the ReccePlanner Agent API.

Endpoints:
  POST /sessions                        → create session, return session_id
  POST /sessions/{id}/upload            → upload one or more PDFs, extract and prime conversation
  POST /sessions/{id}/messages          → send user message (or empty to respond to upload), stream SSE
  GET  /sessions/{id}/latest-plan       → return last generated plan markdown
  GET  /health                          → health check
"""

import os
import uuid
import json
import anthropic
from contextlib import asynccontextmanager
from dotenv import load_dotenv

load_dotenv()  # loads .env if present; no-op in production where env vars are injected directly
from typing import List
from fastapi import FastAPI, HTTPException, UploadFile, File
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import StreamingResponse
from pydantic import BaseModel

from agent import RecceAgent
from pdf_extractor import extract_from_pdf, build_markdown_from_extraction

# ── App lifecycle ─────────────────────────────────────────────────────────────

_anthropic_client: anthropic.AsyncAnthropic | None = None

@asynccontextmanager
async def lifespan(app: FastAPI):
    global _anthropic_client
    api_key = os.getenv("ANTHROPIC_API_KEY")
    if not api_key:
        raise RuntimeError("ANTHROPIC_API_KEY environment variable is required.")
    _anthropic_client = anthropic.AsyncAnthropic(api_key=api_key)
    yield
    await _anthropic_client.close()

app = FastAPI(title="ReccePlanner Agent API", lifespan=lifespan)

_cors_origins = [o.strip() for o in os.getenv("CORS_ORIGINS", "*").split(",")]
app.add_middleware(
    CORSMiddleware,
    allow_origins=_cors_origins,
    allow_methods=["*"],
    allow_headers=["*"],
)

# ── Session store ─────────────────────────────────────────────────────────────
# In-memory for phase 1. Replace with Redis for multi-replica scaling.

_sessions: dict[str, RecceAgent] = {}
_latest_plans: dict[str, str] = {}  # session_id → last plan markdown


# ── Models ────────────────────────────────────────────────────────────────────

class MessageRequest(BaseModel):
    content: str = ""  # empty = stream response to last user message (e.g. after upload)

class SessionResponse(BaseModel):
    session_id: str


# ── Endpoints ─────────────────────────────────────────────────────────────────

@app.get("/health")
def health():
    return {"status": "ok"}


@app.post("/sessions", response_model=SessionResponse)
def create_session():
    session_id = str(uuid.uuid4())
    _sessions[session_id] = RecceAgent(
        _anthropic_client,
        on_plan_ready=lambda plan: _latest_plans.__setitem__(session_id, plan)
    )
    return SessionResponse(session_id=session_id)


@app.post("/sessions/{session_id}/upload")
async def upload_pdfs(session_id: str, files: List[UploadFile] = File(...)):
    """
    Accept one or more PDF uploads, run Claude vision extraction on each,
    merge the results, and prime the conversation with a combined summary
    for the agent to confirm.
    """
    agent = _get_session(session_id)

    if not files:
        raise HTTPException(status_code=400, detail="No files provided.")

    extractions = []
    file_summaries = []

    for file in files:
        if not file.filename.lower().endswith(".pdf"):
            raise HTTPException(status_code=400, detail=f"{file.filename}: only PDF files are accepted.")

        pdf_bytes = await file.read()
        if len(pdf_bytes) == 0:
            raise HTTPException(status_code=400, detail=f"{file.filename}: file is empty.")
        if len(pdf_bytes) > 20 * 1024 * 1024:
            raise HTTPException(status_code=413, detail=f"{file.filename}: too large (max 20 MB).")

        extracted = await extract_from_pdf(pdf_bytes, _anthropic_client)
        extractions.append(extracted)
        file_summaries.append({
            "filename": file.filename,
            "rally_name": extracted.get("rally_name", "Unknown Rally"),
            "stage_count": len(extracted.get("stages", [])),
            "has_travel_times": bool(extracted.get("travel_times", [])),
            "extraction_notes": extracted.get("extraction_notes", "")
        })

    merged = _merge_extractions(extractions)
    rally_markdown = build_markdown_from_extraction(merged)

    # Store the markdown so it's available for download immediately
    _latest_plans[session_id] = rally_markdown

    agent.add_user_message(_build_upload_message(file_summaries, merged, rally_markdown))

    return {
        "files": file_summaries,
        "merged": {
            "rally_name": merged.get("rally_name"),
            "stage_count": len(merged.get("stages", [])),
            "has_travel_times": bool(merged.get("travel_times", []))
        }
    }


@app.post("/sessions/{session_id}/messages")
async def send_message(session_id: str, request: MessageRequest):
    """
    Send a user message and stream the agent response as SSE.
    If content is empty, streams the response to the last queued user message
    (used after upload to get the agent's initial analysis).
    """
    agent = _get_session(session_id)

    if request.content:
        agent.add_user_message(request.content)

    if not agent.ready_to_stream():
        raise HTTPException(
            status_code=400,
            detail="No message to respond to. Upload a file or send a message first."
        )

    async def event_stream():
        async for chunk in agent.stream_response():
            yield chunk

    return StreamingResponse(
        event_stream(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "X-Accel-Buffering": "no"
        }
    )


@app.get("/sessions/{session_id}/latest-plan")
def get_latest_plan(session_id: str):
    _get_session(session_id)  # validates session exists
    plan = _latest_plans.get(session_id)
    if not plan:
        raise HTTPException(status_code=404, detail="No plan generated yet for this session.")
    return {"markdown": plan}


# ── Helpers ───────────────────────────────────────────────────────────────────

def _get_session(session_id: str) -> RecceAgent:
    agent = _sessions.get(session_id)
    if not agent:
        raise HTTPException(status_code=404, detail=f"Session '{session_id}' not found.")
    return agent


def _merge_extractions(extractions: list[dict]) -> dict:
    """Combine stage and travel-time data from multiple PDF extractions."""
    merged: dict = {
        "rally_name": next((e.get("rally_name") for e in extractions if e.get("rally_name")), "Unknown Rally"),
        "stages": [],
        "travel_times": [],
        "extraction_notes": ""
    }
    seen_stages: set = set()
    seen_tt: set = set()
    notes: list[str] = []

    for e in extractions:
        for s in e.get("stages", []):
            if s.get("code") not in seen_stages:
                merged["stages"].append(s)
                seen_stages.add(s["code"])
        for tt in e.get("travel_times", []):
            pair = (tt["from_code"], tt["to_code"])
            if pair not in seen_tt:
                merged["travel_times"].append(tt)
                seen_tt.add(pair)
        if e.get("extraction_notes"):
            notes.append(e["extraction_notes"])

    merged["extraction_notes"] = " | ".join(notes)
    return merged


def _build_upload_message(file_summaries: list[dict], merged: dict, rally_markdown: str) -> str:
    """Build the user message handed to the agent after file upload.

    Includes the pre-formatted ReccePlanner markdown so the agent can pass it
    directly to parse_rally_markdown without having to reconstruct it.
    """
    if len(file_summaries) == 1:
        s = file_summaries[0]
        intro = f"I've uploaded **{s['filename']}** for **{s['rally_name']}**."
    else:
        names = ", ".join(f"**{s['filename']}**" for s in file_summaries)
        intro = f"I've uploaded {len(file_summaries)} files ({names}) for **{merged['rally_name']}**."

    stage_count = len(merged.get("stages", []))
    has_tt = bool(merged.get("travel_times"))
    tt_note = f"{len(merged.get('travel_times', []))} travel time entries extracted" if has_tt \
              else "no travel time matrix found — you will need to provide one"

    notes = merged.get("extraction_notes", "")
    notes_section = f"\n\n**Extraction notes:** {notes}" if notes else ""

    return (
        f"{intro}\n\n"
        f"Extraction summary: **{stage_count} stages**, {tt_note}.{notes_section}\n\n"
        f"The data has been converted to ReccePlanner format and is ready to parse:\n\n"
        f"```\n{rally_markdown}\n```\n\n"
        f"Please parse this, confirm the extracted data looks correct, flag any issues, "
        f"and ask me for recce speed and any time constraints before optimising."
    )
