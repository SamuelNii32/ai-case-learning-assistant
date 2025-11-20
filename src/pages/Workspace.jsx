import { Link, useParams, useSearchParams, useNavigate } from 'react-router-dom'
import React, { useState, useEffect, useRef } from 'react'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import Badge from '@/components/ui/badge'
import { Textarea } from '@/components/ui/textarea'
import WorkspaceNotesPanel from '@/components/WorkspaceNotesPanel.clean'
// Use the working implementation while original file is being cleaned
// Guided mode removed per request
import { PdfControllerProvider } from '@/contexts/pdf-controller'

import { API_BASE } from '@/config'
import {
  buildIndex,
  getAuthToken,
  getUploadSummary,
  createSession,
  getSession,
  listSessionsMine,
} from '@/lib/api'

import toast from 'react-hot-toast'
import {
  Sparkles,
  ArrowLeft,
  MessageSquare,
  Send,
  FileText,
  ImageIcon,
  StickyNote,
  Lightbulb,
  Menu,
  X,
  Clock,
} from 'lucide-react'
// Lazy-load the PDF viewer so heavy pdf.js code is deferred until needed
const PdfViewer = React.lazy(() => import('@/components/PdfViewer.jsx'))
// put this near the top of the file

function appendSmart(prev = '', next = '') {
  if (!next) return prev
  if (!prev) return next

  // Follow tokenizer semantics: tokens that begin with whitespace indicate a
  // separation; tokens that do not begin with whitespace are continuations of
  // the previous token. Therefore, prefer appending the token exactly as
  // emitted and avoid injecting extra spaces which can create artifacts like
  // "sam uel". This keeps punctuation and email joins intact.
  return prev + next
}

export default function Workspace() {
  const { uploadId } = useParams()
  const navigate = useNavigate()

  const [searchParams] = useSearchParams()
  const caseType = searchParams.get('type') || 'personal'

  // Chat-only mode: guided mode has been removed. The workspace always renders chat.

  const [showNotes, setShowNotes] = useState(false)
  const [showHistory, setShowHistory] = useState(false)
  const [showFigures, setShowFigures] = useState(false)
  const [messages, setMessages] = useState([
    {
      role: 'assistant',
      content: "Hello! I've analyzed your case study. What would you like to explore first?",
    },
  ])
  const [message, setMessage] = useState('')
  // Use the local pdf controller state (`pdfCtrl`) which is provided below via PdfControllerProvider.
  // We avoid calling the context consumer here because the Provider is created later in this component.

  // Real conversation history from /sessions/mine
  const [conversationHistory, setConversationHistory] = useState([])
  const [_conversationLoading, setConversationLoading] = useState(false)

  // Active session (for this workspace)
  const [sessionId, setSessionId] = useState(() => {
    return searchParams.get('sessionId') || null
  })
  const [_sessionLoading, setSessionLoading] = useState(false)

  async function handleSendMessage() {
    const text = message.trim()
    if (!text) return

    if (uploadId && indexState === 'ready') {
      const q = text

      // Ensure we have a sessionId for this upload
      let sid = sessionId
      if (!sid) {
        try {
          const created = await createSession(uploadId)
          sid = created?.sessionId || created?.id || null
          if (sid) setSessionId(sid)
        } catch (err) {
          console.error('[Workspace] failed to create session', err)
        }
      }

      // push user message and placeholder assistant
      setMessages(prev => [...prev, { role: 'user', content: q }])
      setMessage('')
      const assistantId = `asst-${Date.now()}`
      setMessages(prev => [
        ...prev,
        { id: assistantId, role: 'assistant', content: '', streaming: true, sources: [] },
      ])

      // use streaming ask endpoint with sessionId (if we have one)
      startAskStream(uploadId, q, assistantId, sid)
      return
    }

    // otherwise fall back to the streaming SSE chat (dev/mock)
    // 1) push the user message
    setMessages(prev => [...prev, { role: 'user', content: text }])
    setMessage('')

    // 2) create a placeholder assistant message that we'll stream into
    const assistantId = `asst-${Date.now()}`
    setMessages(prev => [
      ...prev,
      { id: assistantId, role: 'assistant', content: '', streaming: true, sources: [] },
    ])

    // 3) close any prior stream
    try {
      sseRef.current?.close()
    } catch {
      /* empty */
    }
    sseRef.current = null

    // 4) open SSE to your real backend (proxied via Vite)
    const es = new EventSource(`/api/chat/stream?prompt=${encodeURIComponent(text)}`)
    sseRef.current = es

    // helper to update the single assistant message by id
    const updateAssistant = updater => {
      setMessages(prev => prev.map(m => (m.id === assistantId ? updater(m) : m)))
    }

    es.addEventListener('token', e => {
      const { text } = JSON.parse(e.data)
      updateAssistant(m => ({
        ...m,
        content: appendSmart(m.content || '', text),
      }))
      // keep chat scrolled to bottom while streaming
      requestAnimationFrame(() => scrollToBottom(false))
    })

    es.addEventListener('source', e => {
      const src = JSON.parse(e.data) // { page, label }
      updateAssistant(m => ({
        ...m,
        sources: [src], // single chip for now
      }))
    })

    const finish = () => {
      updateAssistant(m => ({ ...m, streaming: false }))
      // smooth settle to bottom when stream completes
      requestAnimationFrame(() => scrollToBottom(true))
      try {
        es.close()
      } catch {
        /* empty */
      }
      if (sseRef.current === es) sseRef.current = null
    }

    es.addEventListener('done', finish)
    es.addEventListener('error', finish)
  }

  async function handleStartIndex() {
    if (!uploadId) return
    try {
      setIndexState('indexing')
      const summary = await buildIndex(uploadId)
      setIndexSummary(summary)
      setIndexState('ready')
    } catch (err) {
      console.error('Index build failed', err)
      setIndexState('error')
    }
  }

  // Check index status on server: inMemory | onDisk | none
  async function checkIndexStatus(uploadIdParam) {
    if (!uploadIdParam) return
    setIndexState('checking')
    const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
    const url = API_BASE
      ? `${base}/index/status/${encodeURIComponent(uploadIdParam)}`
      : `/index/status/${encodeURIComponent(uploadIdParam)}`

    try {
      const token = getAuthToken()
      const headers = token ? { Authorization: `Bearer ${token}` } : {}
      const res = await fetch(url, { headers })
      if (!res.ok) throw new Error(`Status ${res.status}`)
      const js = await res.json()

      // if index in memory or on disk -> consider it ready (lazy-load from disk)
      if (js?.inMemory === true || js?.onDisk === true) {
        setIndexSummary(prev => prev || { pagesIndexed: js?.chunks ?? null })
        setIndexState('ready')
        return
      }

      // otherwise build index automatically
      setIndexState('indexing')
      try {
        const summary = await buildIndex(uploadIdParam)
        setIndexSummary(summary)
        setIndexState('ready')
      } catch (err) {
        console.error('Auto-build index failed', err)
        setIndexState('error')
      }
    } catch (err) {
      console.error('Failed to fetch index status', err)
      // do not show toast; surface retry via button
      setIndexState('error')
    }
  }

  // docType/classification removed along with guided-mode behavior

  // Start an EventSource stream to the server ask stream endpoint and wire events to the assistant message.
  function startAskStream(uploadIdParam, q, assistantId, sessionIdParam) {
    // close any existing stream/abort controller
    try {
      if (sseRef.current?.abort) sseRef.current.abort()
    } catch {
      /* empty */
    }
    sseRef.current = null

    const enc = encodeURIComponent(q || '')
    const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
    const extra = sessionIdParam ? `&sessionId=${encodeURIComponent(sessionIdParam)}` : ''
    const url = API_BASE
      ? `${base}/ask/stream/${encodeURIComponent(uploadIdParam)}?q=${enc}${extra}`
      : `/ask/stream/${encodeURIComponent(uploadIdParam)}?q=${enc}${extra}`

    const controller = new AbortController()
    sseRef.current = controller

    const token = getAuthToken()
    const headers = token ? { Authorization: `Bearer ${token}` } : {}

    let gotFirstToken = false
    const updateAssistant = updater => {
      setMessages(prev => prev.map(m => (m.id === assistantId ? updater(m) : m)))
    }

    fetch(url, { method: 'GET', headers, signal: controller.signal })
      .then(res => {
        if (!res.ok) throw new Error(`Stream failed: ${res.status}`)
        const reader = res.body.getReader()
        const decoder = new TextDecoder()
        let buffer = ''

        function handleSseBlock(block) {
          // SSE block parser: lines like "event: token" and "data: {...}"
          const lines = block.split('\n').map(l => l.trim())
          let event = null
          let data = ''
          for (const line of lines) {
            if (line.startsWith('event:')) event = line.slice(6).trim()
            else if (line.startsWith('data:')) data += line.slice(5).trim()
            else data += line
          }
          if (!event && data) {
            // try to parse as JSON or fallback to token
            try {
              const j = JSON.parse(data)
              if (j.citations || j.event === 'citations') {
                event = 'citations'
              } else if (j.done || j.event === 'done') {
                event = 'done'
              } else {
                event = 'token'
              }
            } catch {
              event = 'token'
            }
          }
          if (event === 'token') {
            let parsed = null
            try {
              parsed = JSON.parse(data)
            } catch {
              parsed = { text: data }
            }
            const piece = parsed?.text ?? ''
            if (!gotFirstToken) {
              gotFirstToken = true
              updateAssistant(m => ({ ...m, content: '', streaming: true }))
            }
            updateAssistant(m => ({ ...m, content: appendSmart(m.content || '', piece) }))
            requestAnimationFrame(() => scrollToBottom(false))
          } else if (event === 'citations') {
            let arr = []
            try {
              arr = JSON.parse(data)
            } catch {
              arr = []
            }
            updateAssistant(m => ({
              ...m,
              sources: (arr || []).map(p => ({ page: p, label: `p:${p}` })),
            }))
            requestAnimationFrame(() => scrollToBottom(false))
          } else if (event === 'done') {
            updateAssistant(m => {
              const cleaned = (m.content || '')
                .replace(
                  /\b([A-Za-z0-9._%+-]+)\s*@\s*([A-Za-z0-9.-]+)\s*\\.\s*([A-Za-z]{2,})\b/g,
                  '$1@$2.$3'
                )
                .trim()
              return { ...m, content: cleaned, streaming: false }
            })

            // No need to abort here; stream will naturally finish.
            if (sseRef.current === controller) sseRef.current = null
            requestAnimationFrame(() => scrollToBottom(true))
          }
        }

        function readChunk() {
          return reader.read().then(({ done, value }) => {
            if (done) {
              // process any remaining buffer
              if (buffer.trim()) handleSseBlock(buffer)
              return
            }
            buffer += decoder.decode(value, { stream: true })
            // handle SSE-style blocks separated by double-newline
            let idx
            while ((idx = buffer.indexOf('\n\n')) !== -1) {
              const block = buffer.slice(0, idx)
              buffer = buffer.slice(idx + 2)
              handleSseBlock(block)
            }
            // also handle newline-delimited JSON tokens
            const lines = buffer.split('\n')
            for (let i = 0; i < lines.length - 1; i++) {
              const line = lines[i].trim()
              if (!line) continue
              handleSseBlock(line)
            }
            buffer = lines[lines.length - 1]
            return readChunk()
          })
        }

        return readChunk()
      })
      .catch(err => {
        const msg = String(err?.message || err || 'Stream error')

        // 1) If this is just an intentional abort / stream closed, ignore it.
        if (err?.name === 'AbortError' || /aborted/i.test(msg) || /BodyStreamBuffer/i.test(msg)) {
          if (sseRef.current === controller) sseRef.current = null
          return
        }

        // 2) If server indicates index missing, try to build it once and retry
        if (/index/i.test(msg) && !retriedRef.current) {
          retriedRef.current = true
          ;(async () => {
            try {
              await buildIndex(uploadIdParam)
              // restart stream after successful reindex
              try {
                controller.abort()
              } catch {
                /* empty */
              }
              if (sseRef.current === controller) sseRef.current = null
              startAskStream(uploadIdParam, q, assistantId, sessionIdParam)
              return
            } catch (err2) {
              console.error('Reindex retry failed', err2)
              toast.error('Re-index failed; please try again')
            }
          })()
          return
        }

        // 3) Real error: show it in the message and toast
        updateAssistant(m => ({
          ...m,
          streaming: false,
          content: m.content ? `${m.content}\n\nError: ${msg}` : `Error: ${msg}`,
        }))
        toast.error(msg)
        try {
          controller.abort()
        } catch {
          /* empty */
        }
        if (sseRef.current === controller) sseRef.current = null
      })
  }

  function normalizePreview(raw) {
    if (!raw) return 'Conversation started'

    const trimmed = String(raw).trim()

    if (!trimmed || trimmed === '[streamed response]') {
      return 'Conversation started'
    }

    return trimmed
  }

  // Load conversation list for the left "Conversation History" panel
  useEffect(() => {
    let cancelled = false

    async function loadConversations() {
      try {
        setConversationLoading(true)
        const sessions = await listSessionsMine()

        if (cancelled) return

        const mapped = Array.isArray(sessions)
          ? sessions.map(s => {
              const last = s.lastActivityAt || s.createdAt
              const dateLabel = last
                ? new Date(last).toLocaleDateString('en-US', {
                    month: 'short',
                    day: 'numeric',
                  })
                : '—'

              return {
                id: s.sessionId,
                caseId: s.uploadId,
                title: s.caseName || 'Untitled case',
                date: dateLabel,
                preview: normalizePreview(s.lastMessagePreview),
                messageCount: s.messageCount ?? 0,
              }
            })
          : []

        setConversationHistory(mapped)
      } catch (err) {
        console.error('[Workspace] failed to load conversation history', err)
      } finally {
        if (!cancelled) setConversationLoading(false)
      }
    }

    loadConversations()
    return () => {
      cancelled = true
    }
  }, [])

  // If a sessionId is present in the URL, load its message history
  useEffect(() => {
    const fromUrl = searchParams.get('sessionId')
    if (!uploadId || !fromUrl) {
      // No explicit session – keep greeting and create on first question
      return
    }

    let cancelled = false

    async function initSessionFromUrl() {
      try {
        setSessionLoading(true)
        setSessionId(fromUrl)

        const history = await getSession(fromUrl)
        if (cancelled) return

        if (Array.isArray(history) && history.length > 0) {
          setMessages(
            history.map(m => ({
              role: m.role === 'user' ? 'user' : 'assistant',
              content: m.content || '',
              sources: Array.isArray(m.pagesUsed)
                ? m.pagesUsed.map(p => ({ page: p, label: `p:${p}` }))
                : [],
              createdAt: m.createdAt,
            }))
          )
        } else {
          // No prior messages -> keep friendly greeting
          setMessages([
            {
              role: 'assistant',
              content:
                "Hello! I've analyzed your case study. What would you like to explore first?",
            },
          ])
        }
      } catch (err) {
        console.error('[Workspace] failed to load session messages', err)
      } finally {
        if (!cancelled) setSessionLoading(false)
      }
    }

    initSessionFromUrl()
    return () => {
      cancelled = true
    }
  }, [uploadId, searchParams])

  useEffect(() => {
    return () => {
      if (import.meta.env.DEV) delete window.pdfCtrl
    }
  }, [])

  useEffect(() => {
    // Only lock body scroll and aria-hide the main content for history/figures.
    const blocking = showHistory || showFigures
    const prev = document.body.style.overflow
    if (blocking) document.body.style.overflow = 'hidden'
    return () => {
      document.body.style.overflow = prev
    }
  }, [showHistory, showFigures])

  const pdfCtrlRef = useRef(null)
  const [pdfCtrl, setPdfCtrl] = useState(null)
  const [caseTitle, setCaseTitle] = useState(null)
  const [uploadDate, setUploadDate] = useState(null)
  const [indexState, setIndexState] = useState('not-indexed') // 'not-indexed' | 'indexing' | 'ready' | 'error'
  const [indexSummary, setIndexSummary] = useState(null)
  const sseRef = useRef(null)
  const retriedRef = useRef(false)

  const chatRef = useRef(null)

  const scrollToBottom = (smooth = false) => {
    const el = chatRef.current
    if (!el) return
    try {
      el.scrollTo({ top: el.scrollHeight, behavior: smooth ? 'smooth' : 'auto' })
    } catch {
      el.scrollTop = el.scrollHeight
    }
  }

  useEffect(() => {
    // pdfReady flag removed; PDF controller reset
    pdfCtrlRef.current = null
    // reset index state when switching uploads
    setIndexSummary(null)
    retriedRef.current = false
    if (uploadId) {
      checkIndexStatus(uploadId)
    } else {
      setIndexState('not-indexed')
    }
    // tutor/guided state removed
    // fetch upload metadata (title) for header display
    if (uploadId) {
      ;(async () => {
        try {
          const meta = await getUploadSummary(uploadId)
          // prefer explicit title, fall back to originalFileName or filename
          const t =
            meta?.title || meta?.originalFileName || meta?.fileName || meta?.filename || null
          setCaseTitle(t)

          // extract an upload date from common server fields and format it for display
          const rawDate =
            meta?.uploadedAt ?? meta?.createdAt ?? meta?.uploaded_at ?? meta?.uploaded_at_ms ?? null
          let formatted = null
          if (rawDate) {
            let dt = null
            try {
              // handle number-like strings and numeric timestamps (seconds vs ms)
              if (typeof rawDate === 'number') {
                dt = rawDate < 1e12 ? new Date(rawDate * 1000) : new Date(rawDate)
              } else {
                const asNum = Number(rawDate)
                if (!Number.isNaN(asNum)) {
                  dt = String(rawDate).length <= 10 ? new Date(asNum * 1000) : new Date(asNum)
                } else {
                  dt = new Date(rawDate)
                }
              }
              if (!isNaN(dt.getTime())) formatted = dt.toLocaleString()
            } catch {
              formatted = null
            }
          }
          setUploadDate(formatted)
        } catch (err) {
          console.debug('[Workspace] failed to fetch upload summary', err)
          setCaseTitle(null)
          setUploadDate(null)
        }
      })()
    } else {
      setCaseTitle(null)
    }
  }, [uploadId])

  // Guided mode removed; no mode gating required.

  useEffect(() => {
    // Ensure any open SSE/EventSource is closed promptly:
    // - when the component unmounts
    // - when the page is being hidden or unloaded (pagehide/visibilitychange)
    // Closing early helps the page be eligible for bfcache and avoids
    // leaving persistent connections open across navigations.
    const handlePageHide = () => {
      try {
        sseRef.current?.close?.()
      } catch {
        /* empty */
      }
      sseRef.current = null
    }

    const handleVisibilityChange = () => {
      if (document.visibilityState === 'hidden') handlePageHide()
    }

    window.addEventListener('pagehide', handlePageHide, { capture: true })
    document.addEventListener('visibilitychange', handleVisibilityChange)

    return () => {
      try {
        window.removeEventListener('pagehide', handlePageHide, { capture: true })
        document.removeEventListener('visibilitychange', handleVisibilityChange)
      } catch {
        /* ignore */
      }
      try {
        sseRef.current?.close?.()
      } catch {
        /* empty */
      }
      sseRef.current = null
    }
  }, [])

  // Clean up controller reference when the uploadId changes or component unmounts

  const closeHistoryBtnRef = useRef(null)
  useEffect(() => {
    if (showHistory) closeHistoryBtnRef.current?.focus()
  }, [showHistory])

  const closeFiguresBtnRef = useRef(null)
  useEffect(() => {
    if (showFigures) closeFiguresBtnRef.current?.focus()
  }, [showFigures])

  const notesPanelRef = useRef(null)
  useEffect(() => {
    if (showNotes) notesPanelRef.current?.focus()
  }, [showNotes])

  return (
    <PdfControllerProvider value={pdfCtrl}>
      <div
        className="h-screen bg-white flex flex-col"
        data-mode="chat"
        data-shownotes={String(showNotes)}
        data-showhistory={String(showHistory)}
        data-showfigures={String(showFigures)}
        data-uploadid={uploadId || ''}
        data-casetype={caseType}
      >
        {/* Header */}
        <header className="h-14 border-b border-border bg-card/50 backdrop-blur-sm flex-shrink-0">
          <div className="container mx-auto px-4 h-full flex items-center justify-between">
            <div className="flex items-center gap-4 flex-1 min-w-0">
              <Button
                variant="ghost"
                size="sm"
                className="lg:hidden"
                onClick={() => setShowHistory(v => !v)}
              >
                <Menu className="w-4 h-4" />
              </Button>

              {/* Render Link styled like a ghost Button to avoid nested anchors */}
              <Link
                to="/dashboard"
                className={
                  'inline-flex items-center justify-center rounded-md text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:opacity-50 disabled:pointer-events-none hover:bg-slate-100 h-9 px-3 gap-2'
                }
              >
                <span className="flex items-center gap-2">
                  <ArrowLeft className="w-4 h-4" />
                  <span className="hidden sm:inline">Back to Dashboard</span>
                </span>
              </Link>

              <div className="h-6 w-px bg-border hidden sm:block" />

              <div className="flex items-center gap-2 min-w-0 flex-1">
                <FileText className="w-4 h-4 text-muted-foreground flex-shrink-0" />
                <div className="flex items-center gap-2 min-w-0">
                  <span className="text-sm font-medium text-foreground truncate">
                    {caseTitle || 'Healthcare Innovation Case'}
                  </span>
                  {uploadDate && (
                    <span className="text-xs text-muted-foreground ml-2 whitespace-nowrap">
                      {uploadDate}
                    </span>
                  )}
                </div>
                {(searchParams.get('type') || 'personal') === 'assigned' && (
                  <Badge variant="secondary" className="ml-2">
                    Assigned
                  </Badge>
                )}
              </div>
            </div>

            <div className="flex items-center gap-2 flex-shrink-0">
              <div className="hidden md:flex items-center gap-1 bg-muted rounded-lg p-1">
                <Button variant="secondary" size="sm" className="text-xs">
                  <MessageSquare className="w-3 h-3 mr-1" />
                  Chat
                </Button>
                {/* Guided mode removed — button intentionally omitted */}
              </div>

              <Button
                variant="outline"
                size="sm"
                className="gap-2 bg-transparent"
                onClick={() => setShowNotes(true)}
              >
                <StickyNote className="w-4 h-4" />
                <span className="hidden sm:inline">Notes</span>
              </Button>

              {/* DEV helpers removed: jump/flash buttons were for local debugging */}
            </div>
          </div>
        </header>

        {/* Main two-pane layout (left PDF, right chat/guided) */}
        <div aria-hidden={showHistory || showFigures || showNotes ? 'true' : 'false'}>
          <div className="flex-1 flex flex-col md:flex-row overflow-hidden">
            {/* Permanent sidebar on md+; falls back to drawer on small screens */}
            <aside className="hidden lg:flex lg:flex-col w-80 border-r border-border bg-white overflow-auto">
              <div className="p-4 space-y-4">
                <div className="flex items-center justify-between">
                  <h3 className="font-semibold text-foreground">Conversation History</h3>
                </div>

                <div className="space-y-2">
                  {conversationHistory.map(c => (
                    <Card
                      key={c.id}
                      className="p-3 cursor-pointer hover:border-primary/50 transition-colors"
                      onClick={() => {
                        // uploadId for this conversation (PDF case). Fallback to sessionId if ever needed.
                        const uploadIdForNav = c.caseId || c.id

                        const url = `/workspace/${encodeURIComponent(
                          uploadIdForNav
                        )}?sessionId=${encodeURIComponent(c.id)}`

                        navigate(url)
                        setShowHistory(false)
                      }}
                    >
                      <div className="space-y-2">
                        <div className="flex items-start justify-between gap-2">
                          <h4 className="text-sm font-medium text-foreground line-clamp-1">
                            {c.title}
                          </h4>
                          <span className="text-xs text-muted-foreground whitespace-nowrap">
                            {c.messageCount}
                          </span>
                        </div>
                        <p className="text-xs text-muted-foreground line-clamp-2">{c.preview}</p>
                        <div className="flex items-center gap-1 text-xs text-muted-foreground">
                          <Clock className="w-3 h-3" />
                          {c.date}
                        </div>
                      </div>
                    </Card>
                  ))}
                </div>
              </div>
            </aside>

            <div className="flex-1 md:flex-1 min-w-0 border-r border-border bg-muted/30 overflow-auto">
              <div className="p-6 max-w-3xl mx-auto">
                <Card className="bg-card">
                  <div className="p-4">
                    {uploadId ? (
                      <div className="w-full h-[70vh] bg-white rounded overflow-hidden border border-border relative">
                        <React.Suspense
                          fallback={
                            <div className="flex items-center justify-center h-full text-sm text-muted-foreground">
                              Loading preview…
                            </div>
                          }
                        >
                          <PdfViewer
                            src={
                              API_BASE
                                ? `${String(API_BASE).replace(/\/$/, '')}/uploads/${uploadId}.pdf`
                                : `/uploads/${uploadId}.pdf`
                            }
                            unmirror
                            fitToWidth
                            onReady={ctrl => {
                              pdfCtrlRef.current = ctrl
                              setPdfCtrl(ctrl) // <-- context value
                              if (import.meta.env.DEV) {
                                window.pdfCtrl = ctrl
                                console.log('[Workspace] pdfCtrl ready:', ctrl)
                              }
                            }}
                          />
                        </React.Suspense>
                        {/* small toolbar (top-right) so users can open Figures & Charts while viewing a PDF */}
                        <div className="absolute top-2 right-2 z-20">
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => setShowFigures(v => !v)}
                            aria-label={showFigures ? 'Close figures panel' : 'Open figures panel'}
                          >
                            <ImageIcon className="w-4 h-4" />
                          </Button>
                        </div>
                      </div>
                    ) : (
                      <div className="p-8 space-y-4">
                        <div className="flex items-center justify-between pb-4 border-b border-border">
                          <span className="text-sm text-muted-foreground">Page 1 of 12</span>
                          <div className="flex items-center gap-2">
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => setShowFigures(v => !v)}
                            >
                              <ImageIcon className="w-4 h-4" />
                            </Button>
                          </div>
                        </div>

                        <div className="space-y-4 text-sm leading-relaxed">
                          <h2 className="text-2xl font-bold text-foreground">
                            {caseTitle ? caseTitle : 'Healthcare Innovation Case Study'}
                          </h2>

                          <p className="text-foreground">
                            In 2023, MediTech Solutions faced a critical challenge in their
                            emergency department operations. Patient wait times had increased by 40%
                            over the previous year, leading to decreased satisfaction scores and
                            potential safety concerns.
                          </p>

                          <div className="pdf-highlight p-3 rounded-md">
                            <p className="text-foreground font-medium">
                              The primary issue stemmed from inefficient triage processes and lack
                              of real-time bed availability tracking across the hospital network.
                            </p>
                          </div>

                          <p className="text-foreground">
                            The hospital's leadership team recognized the need for a comprehensive
                            digital transformation strategy. They assembled a cross-functional team
                            including clinicians, IT specialists, and operations managers to address
                            the challenge.
                          </p>

                          <p className="text-foreground">
                            Initial analysis revealed several contributing factors: outdated
                            communication systems, manual data entry processes, and siloed
                            information between departments. The team needed to develop a solution
                            that would integrate seamlessly with existing workflows while improving
                            efficiency.
                          </p>

                          <div className="bg-muted p-4 rounded-lg space-y-2">
                            <p className="text-xs text-muted-foreground font-medium">KEY METRICS</p>
                            <div className="grid grid-cols-3 gap-4 text-sm">
                              <div>
                                <p className="text-muted-foreground">Avg Wait Time</p>
                                <p className="text-foreground font-semibold">4.2 hours</p>
                              </div>
                              <div>
                                <p className="text-muted-foreground">Satisfaction</p>
                                <p className="text-foreground font-semibold">62%</p>
                              </div>
                              <div>
                                <p className="text-muted-foreground">Capacity</p>
                                <p className="text-foreground font-semibold">87%</p>
                              </div>
                            </div>
                          </div>
                        </div>
                      </div>
                    )}
                  </div>
                </Card>
              </div>
            </div>

            <div className="w-full md:flex-1 lg:w-[480px] flex-shrink-0 flex flex-col bg-card">
              <>
                {/* Messages */}
                <div
                  ref={chatRef}
                  className={`flex-1 overflow-auto p-4 space-y-4 ${showFigures ? 'pr-64' : ''}`}
                >
                  {/* tutor gating removed: guided mode intentionally disabled for now */}

                  {messages.map((msg, idx) => {
                    const isUser = msg.role === 'user'
                    return (
                      <div
                        key={msg.id ?? idx}
                        className={`flex ${isUser ? 'justify-end' : 'justify-start'}`}
                      >
                        <div
                          className={`max-w-[85%] rounded-lg p-3 ${
                            isUser
                              ? 'bg-primary text-primary-foreground'
                              : 'bg-muted text-foreground'
                          }`}
                        >
                          <p className="text-sm leading-relaxed whitespace-pre-wrap break-words">
                            {msg.content}
                            {msg.streaming && (
                              <span className="ml-2 text-muted-foreground" aria-hidden="true">
                                <span className="typing-dots" aria-hidden="true">
                                  <span />
                                  <span />
                                  <span />
                                </span>
                              </span>
                            )}
                          </p>

                          {/* ↓ Source chip(s) */}
                          {!isUser && msg.sources && msg.sources.length > 0 && (
                            <div className="mt-2 flex gap-2">
                              {msg.sources.map((s, i) => (
                                <button
                                  key={i}
                                  type="button"
                                  disabled={!pdfCtrl}
                                  onClick={() => pdfCtrl?.showHighlight({ page: s.page })}
                                  className="text-xs px-2 py-1 rounded-full border border-border bg-white hover:bg-muted disabled:opacity-50"
                                  title={`Open ${s.label}`}
                                >
                                  {s.label}
                                </button>
                              ))}
                            </div>
                          )}
                        </div>
                      </div>
                    )
                  })}

                  {/* Suggested Questions - hidden on mobile, visible md+ */}
                  <div className="pt-4 space-y-2 hidden md:block">
                    <p className="text-xs text-muted-foreground flex items-center gap-1">
                      <Lightbulb className="w-3 h-3" />
                      Suggested Questions
                    </p>
                    <div className="space-y-2">
                      {[
                        'What is the main problem?',
                        'What evidence supports this?',
                        'What are potential solutions?',
                      ].map((q, i) => (
                        <Button
                          key={i}
                          variant="outline"
                          size="sm"
                          className="w-full justify-start items-start text-left h-auto py-2 px-3 bg-transparent"
                          onClick={() => setMessage(q)}
                        >
                          <div className="w-full flex items-start justify-start">
                            <span className="text-xs font-normal">{q}</span>
                          </div>
                        </Button>
                      ))}
                    </div>
                  </div>
                </div>

                {/* Input */}
                <div className="border-t border-border p-4">
                  {/* Indexing control */}
                  {uploadId && indexState !== 'ready' && (
                    <div className="mb-3 flex items-center gap-2">
                      <Button
                        size="sm"
                        variant={
                          indexState === 'indexing' || indexState === 'checking'
                            ? 'secondary'
                            : 'outline'
                        }
                        onClick={handleStartIndex}
                        disabled={indexState === 'indexing' || indexState === 'checking'}
                      >
                        {indexState === 'not-indexed' && 'Start Q&A'}
                        {indexState === 'checking' && 'Checking…'}
                        {indexState === 'indexing' && 'Indexing…'}
                        {indexState === 'error' && 'Rebuild index'}
                      </Button>
                    </div>
                  )}
                  {indexState === 'ready' && indexSummary && (
                    <div className="mb-3 text-xs text-muted-foreground">
                      {indexSummary.pagesIndexed || indexSummary.indexed || indexSummary.count
                        ? `${indexSummary.pagesIndexed || indexSummary.indexed || indexSummary.count} pages indexed`
                        : 'Index ready'}
                    </div>
                  )}
                  <div className="flex gap-2 items-start">
                    <div className="flex-1">
                      <Textarea
                        placeholder="Ask about the case..."
                        value={message}
                        onChange={e => setMessage(e.target.value)}
                        onKeyDown={e => {
                          if (e.key === 'Enter' && !e.shiftKey) {
                            e.preventDefault()
                            handleSendMessage()
                          }
                        }}
                        className="w-full min-h-[40px] max-h-[96px] resize-none"
                      />
                    </div>

                    <div className="flex-shrink-0 self-start -translate-y-1">
                      <Button
                        size="icon"
                        onClick={handleSendMessage}
                        aria-label="Send message"
                        className="h-10 w-10"
                      >
                        <Send className="w-4 h-4" />
                      </Button>
                    </div>
                  </div>
                </div>
              </>
            </div>
          </div>
        </div>

        {showHistory && (
          <>
            {/* Backdrop - only show on small screens and tablet (hidden on lg+) */}
            <div
              className="fixed inset-0 bg-black/50 backdrop-blur-sm z-40 lg:hidden"
              onClick={() => setShowHistory(false)}
            />

            {/* Drawer - only show on small screens and tablet (hidden on lg+) */}
            <div className="fixed top-14 bottom-0 left-0 w-80 bg-white border-r border-border shadow-xl z-50 overflow-auto lg:hidden">
              <div className="p-4 space-y-4">
                <div className="flex items-center justify-between">
                  <h3 className="font-semibold text-foreground">Conversation History</h3>
                  <Button
                    ref={closeHistoryBtnRef}
                    variant="ghost"
                    size="sm"
                    onClick={() => setShowHistory(false)}
                    aria-label="Close conversation history"
                  >
                    <X className="w-4 h-4" />
                  </Button>
                </div>

                <Button className="w-full" size="sm">
                  <MessageSquare className="w-4 h-4 mr-2" />
                  New Conversation
                </Button>

                <div className="space-y-2">
                  {conversationHistory.map(c => (
                    <Card
                      key={c.id}
                      className="p-3 cursor-pointer hover:border-primary/50 transition-colors"
                      onClick={() => {
                        const uploadIdForNav = c.caseId || c.id

                        const url = `/workspace/${encodeURIComponent(
                          uploadIdForNav
                        )}?sessionId=${encodeURIComponent(c.id)}`

                        navigate(url)
                        setShowHistory(false)
                      }}
                    >
                      <div className="space-y-2">
                        <div className="flex items-start justify-between gap-2">
                          <h4 className="text-sm font-medium text-foreground line-clamp-1">
                            {c.title}
                          </h4>
                          <span className="text-xs text-muted-foreground whitespace-nowrap">
                            {c.messageCount}
                          </span>
                        </div>
                        <p className="text-xs text-muted-foreground line-clamp-2">{c.preview}</p>
                        <div className="flex items-center gap-1 text-xs text-muted-foreground">
                          <Clock className="w-3 h-3" />
                          {c.date}
                        </div>
                      </div>
                    </Card>
                  ))}
                </div>
              </div>
            </div>
          </>
        )}

        {showFigures && (
          <div className="fixed top-0 bottom-0 right-0 w-64 bg-white border-l border-border shadow-xl z-60 overflow-auto">
            <div className="p-4 space-y-4">
              <div className="flex items-center justify-between">
                <h3 className="font-semibold text-foreground">Figures & Charts</h3>
                <Button
                  ref={closeFiguresBtnRef}
                  variant="ghost"
                  size="sm"
                  onClick={() => setShowFigures(false)}
                  aria-label="Close figures panel"
                >
                  ×
                </Button>
              </div>

              <div className="space-y-3">
                {[1, 2, 3].map(num => {
                  const targetPage = num + 2
                  const disabled = !pdfCtrl
                  return (
                    <Card
                      key={num}
                      className={`p-3 cursor-pointer hover:border-primary/50 transition-colors ${disabled ? 'opacity-50 cursor-not-allowed' : ''}`}
                      onClick={() => {
                        if (!pdfCtrl) return
                        pdfCtrl.showHighlight({ page: targetPage }) // this scrolls + flashes
                      }}
                      aria-disabled={disabled}
                      role="button"
                      tabIndex={disabled ? -1 : 0}
                    >
                      <div className="aspect-video bg-muted rounded mb-2 flex items-center justify-center" />
                      <p className="text-xs text-muted-foreground">Figure {num}</p>
                      <p className="text-xs text-foreground font-medium">
                        Chart on page {targetPage}
                      </p>
                    </Card>
                  )
                })}
              </div>
            </div>
          </div>
        )}
        <WorkspaceNotesPanel
          open={showNotes}
          onOpenChange={setShowNotes}
          currentCaseId={uploadId}
          currentSessionId={sessionId || 'session-current'}
          panelRef={notesPanelRef}
        />
      </div>
    </PdfControllerProvider>
  )
}
