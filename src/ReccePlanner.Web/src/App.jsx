import { useState, useRef, useEffect, useCallback } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'

// ── API ───────────────────────────────────────────────────────────────────────
// VITE_API_URL is set at build time for production. Empty in dev (Vite proxies).

const BASE = import.meta.env.VITE_API_URL ?? ''

async function apiCreateSession() {
  const r = await fetch(`${BASE}/sessions`, { method: 'POST' })
  if (!r.ok) throw new Error(`Could not start session (${r.status})`)
  return (await r.json()).session_id
}

async function apiUpload(sessionId, files) {
  const form = new FormData()
  for (const f of files) form.append('files', f)
  const r = await fetch(`${BASE}/sessions/${sessionId}/upload`, { method: 'POST', body: form })
  if (!r.ok) {
    const body = await r.json().catch(() => ({}))
    throw new Error(body.detail ?? `Upload failed (${r.status})`)
  }
  return r.json()
}

async function apiFetchPlan(sessionId) {
  const r = await fetch(`${BASE}/sessions/${sessionId}/latest-plan`)
  if (!r.ok) throw new Error('No plan available yet')
  return (await r.json()).markdown
}

async function* apiStream(sessionId, content) {
  const r = await fetch(`${BASE}/sessions/${sessionId}/messages`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ content })
  })
  if (!r.ok) {
    const body = await r.json().catch(() => ({}))
    throw new Error(body.detail ?? `Request failed (${r.status})`)
  }

  const reader = r.body.getReader()
  const decoder = new TextDecoder()
  let buf = ''

  while (true) {
    const { done, value } = await reader.read()
    if (done) break
    buf += decoder.decode(value, { stream: true })
    // SSE events are separated by double newlines
    const parts = buf.split('\n\n')
    buf = parts.pop()
    for (const part of parts) {
      for (const line of part.split('\n')) {
        if (line.startsWith('data: ')) {
          try { yield JSON.parse(line.slice(6)) } catch { /* ignore malformed */ }
        }
      }
    }
  }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function downloadBlob(content, filename) {
  const a = document.createElement('a')
  a.href = URL.createObjectURL(new Blob([content], { type: 'text/markdown' }))
  a.download = filename
  a.click()
  URL.revokeObjectURL(a.href)
}

const TOOL_LABELS = {
  parse_rally_markdown:  'Parsing stage data',
  validate_travel_times: 'Validating travel times',
  optimize_recce:        'Optimizing route',
  analyze_two_day_split: 'Analysing two-day split',
  generate_recce_plan:   'Generating plan',
}

// ── Sub-components ────────────────────────────────────────────────────────────

function UploadZone({ onFiles, disabled }) {
  const [dragging, setDragging] = useState(false)
  const inputRef = useRef(null)
  const dragCount = useRef(0)  // count enter/leave pairs to avoid child-element flicker

  const pick = e => { e.stopPropagation(); if (!disabled) inputRef.current.click() }

  const onDragEnter = e => { e.preventDefault(); dragCount.current++; setDragging(true) }
  const onDragLeave = e => { e.preventDefault(); dragCount.current--; if (dragCount.current === 0) setDragging(false) }
  const onDragOver  = e => { e.preventDefault() }  // required to allow drop

  const onDrop = e => {
    e.preventDefault()
    dragCount.current = 0
    setDragging(false)
    if (!disabled) onFiles(e.dataTransfer.files)
  }

  return (
    <div
      className={`upload-zone${dragging ? ' dragging' : ''}${disabled ? ' disabled' : ''}`}
      onClick={pick}
      onDragEnter={onDragEnter}
      onDragOver={onDragOver}
      onDragLeave={onDragLeave}
      onDrop={onDrop}
    >
      <input ref={inputRef} type="file" accept=".pdf" multiple hidden
        onChange={e => { onFiles(e.target.files); e.target.value = '' }} />
      <div className="upload-icon">&#128196;</div>
      <p className="upload-primary">Drop PDF files here or click to browse</p>
      <p className="upload-secondary">
        Rally supplemental regulations, travel time sheets&mdash;multiple files welcome
      </p>
    </div>
  )
}

function FileBar({ files }) {
  return (
    <div className="file-bar">
      {files.map(f => (
        <span key={f.name} className={`file-chip ${f.status}`}>
          {f.status === 'done' ? '✓' : f.status === 'error' ? '✗' : '…'} {f.name}
        </span>
      ))}
    </div>
  )
}

function Message({ msg }) {
  if (msg.role === 'system') {
    return (
      <div className="msg-system">
        <span>{msg.content}</span>
      </div>
    )
  }

  return (
    <div className={`msg msg-${msg.role}`}>
      {msg.role === 'assistant' && <div className="avatar">R</div>}
      <div className="bubble">
        {msg.tool && (
          <div className="tool-indicator">
            <span className="spinner" />
            {TOOL_LABELS[msg.tool] ?? msg.tool}&hellip;
          </div>
        )}
        <ReactMarkdown remarkPlugins={[remarkGfm]}>{msg.content}</ReactMarkdown>
      </div>
      {msg.role === 'user' && <div className="avatar avatar-user">U</div>}
    </div>
  )
}

// ── App ───────────────────────────────────────────────────────────────────────

export default function App() {
  const [sessionId, setSessionId]     = useState(null)
  const [files, setFiles]             = useState([])      // {name, status}
  const [messages, setMessages]       = useState([])      // {id, role, content, tool?}
  const [input, setInput]             = useState('')
  const [busy, setBusy]               = useState(false)
  const [planReady, setPlanReady]     = useState(false)

  const sessionRef = useRef(null)  // stable ref for async callbacks
  const bottomRef  = useRef(null)
  const attachRef  = useRef(null)
  const textRef    = useRef(null)

  useEffect(() => { sessionRef.current = sessionId }, [sessionId])
  useEffect(() => { bottomRef.current?.scrollIntoView({ behavior: 'smooth' }) }, [messages])

  // Prevent browser from navigating to a file when dropped outside the upload zone
  useEffect(() => {
    const prevent = e => e.preventDefault()
    window.addEventListener('dragover', prevent)
    window.addEventListener('drop', prevent)
    return () => { window.removeEventListener('dragover', prevent); window.removeEventListener('drop', prevent) }
  }, [])

  // Auto-grow textarea
  const resizeTextarea = () => {
    const el = textRef.current
    if (!el) return
    el.style.height = 'auto'
    el.style.height = Math.min(el.scrollHeight, 150) + 'px'
  }

  // ── Session ──────────────────────────────────────────────────────────────

  const getOrCreate = useCallback(async () => {
    if (sessionRef.current) return sessionRef.current
    const id = await apiCreateSession()
    setSessionId(id)
    sessionRef.current = id
    return id
  }, [])

  // ── Stream helper ─────────────────────────────────────────────────────────

  const runStream = useCallback(async (sid, content) => {
    const id = crypto.randomUUID()
    setMessages(prev => [...prev, { id, role: 'assistant', content: '', tool: null }])

    try {
      for await (const ev of apiStream(sid, content)) {
        if (ev.type === 'text') {
          setMessages(prev => prev.map(m => m.id === id
            ? { ...m, content: m.content + ev.content } : m))
        } else if (ev.type === 'tool_start') {
          setMessages(prev => prev.map(m => m.id === id ? { ...m, tool: ev.name } : m))
        } else if (ev.type === 'tool_end') {
          setMessages(prev => prev.map(m => m.id === id ? { ...m, tool: null } : m))
        } else if (ev.type === 'plan_ready') {
          setPlanReady(true)
        } else if (ev.type === 'done') {
          break
        }
      }
    } catch (err) {
      setMessages(prev => prev.map(m => m.id === id
        ? { ...m, content: m.content || `Error: ${err.message}` } : m))
    }
  }, [])

  // ── File upload ───────────────────────────────────────────────────────────

  const handleFiles = useCallback(async (fileList) => {
    const pdfs = Array.from(fileList).filter(f => f.name.toLowerCase().endsWith('.pdf'))
    if (!pdfs.length) return

    setBusy(true)
    const names = pdfs.map(f => f.name).join(', ')
    setFiles(prev => [...prev, ...pdfs.map(f => ({ name: f.name, status: 'uploading' }))])

    // Show immediate feedback — extraction can take 30-60 s with no other signal
    const extractMsgId = crypto.randomUUID()
    setMessages(prev => [...prev, {
      id: extractMsgId,
      role: 'system',
      content: `Extracting data from ${names} using Claude vision — this may take up to a minute...`
    }])

    try {
      const sid = await getOrCreate()
      await apiUpload(sid, pdfs)
      setFiles(prev => prev.map(f =>
        pdfs.find(p => p.name === f.name) ? { ...f, status: 'done' } : f))
      // Replace the extraction notice with a cleaner "done" note before the agent responds
      setMessages(prev => prev.map(m =>
        m.id === extractMsgId
          ? { ...m, content: `PDF extraction complete. Agent is reviewing the data...` }
          : m
      ))
      await runStream(sid, '')  // stream agent's response to the upload summary
      // Remove the status message once the agent has responded
      setMessages(prev => prev.filter(m => m.id !== extractMsgId))
    } catch (err) {
      setFiles(prev => prev.map(f =>
        pdfs.find(p => p.name === f.name) ? { ...f, status: 'error' } : f))
      setMessages(prev => prev.map(m =>
        m.id === extractMsgId
          ? { ...m, content: `Upload failed: ${err.message}` }
          : m
      ))
    } finally {
      setBusy(false)
    }
  }, [getOrCreate, runStream])

  // ── Send message ──────────────────────────────────────────────────────────

  const handleSend = useCallback(async () => {
    const text = input.trim()
    if (!text || busy) return
    setInput('')
    if (textRef.current) textRef.current.style.height = 'auto'
    setBusy(true)
    setMessages(prev => [...prev, { id: crypto.randomUUID(), role: 'user', content: text }])
    try {
      const sid = await getOrCreate()
      await runStream(sid, text)
    } finally {
      setBusy(false)
    }
  }, [input, busy, getOrCreate, runStream])

  // ── Download ──────────────────────────────────────────────────────────────

  const handleDownload = useCallback(async () => {
    try {
      const plan = await apiFetchPlan(sessionRef.current)
      downloadBlob(plan, 'recce-plan.md')
    } catch (err) {
      alert(err.message)
    }
  }, [])

  // ── Key handler ───────────────────────────────────────────────────────────

  const onKeyDown = e => {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); handleSend() }
  }

  const hasActivity = messages.length > 0 || files.length > 0

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="app">

      <header className="header">
        <div className="header-brand">
          <img src="/logo.png" alt="Pura Vida Rally Team" className="header-logo" />
          <span className="header-title">ReccePlanner</span>
        </div>
        {planReady && (
          <button className="btn-download" onClick={handleDownload}>
            &#8595;&nbsp;Download Plan
          </button>
        )}
      </header>

      <main className="main">

        {!hasActivity && (
          <UploadZone onFiles={handleFiles} disabled={busy} />
        )}

        {files.length > 0 && <FileBar files={files} />}

        <div className="messages">
          {messages.map(m => <Message key={m.id} msg={m} />)}
          <div ref={bottomRef} />
        </div>

      </main>

      <div className="input-bar">
        <button
          className="btn-attach"
          title="Add more files"
          disabled={busy}
          onClick={() => attachRef.current.click()}
        >
          &#128206;
        </button>
        <input ref={attachRef} type="file" accept=".pdf" multiple hidden
          onChange={e => { handleFiles(e.target.files); e.target.value = '' }} />
        <textarea
          ref={textRef}
          className="input-text"
          value={input}
          rows={1}
          disabled={busy}
          placeholder={hasActivity ? 'Ask about the recce plan...' : 'Or type a message to start...'}
          onChange={e => { setInput(e.target.value); resizeTextarea() }}
          onKeyDown={onKeyDown}
        />
        <button
          className="btn-send"
          disabled={!input.trim() || busy}
          onClick={handleSend}
        >
          Send
        </button>
      </div>

    </div>
  )
}
