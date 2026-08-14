import { Link, useParams, useSearchParams, useNavigate } from 'react-router-dom'
import React, { useState, useEffect, useRef } from 'react'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import Badge from '@/components/ui/badge'
import { Textarea } from '@/components/ui/textarea'
import WorkspaceNotesPanel from '@/components/WorkspaceNotesPanel.clean'
import GuidedModeFlow from '@/components/GuidedModeFlow'
import { PdfControllerProvider } from '@/contexts/pdf-controller'
import initPdfWorker from '@/lib/pdfjs-setup'

import { API_BASE } from '@/config'
import {
  buildIndex,
  getAuthToken,
  getPagedItems,
  getUploadSummary,
  createSession,
  getSession,
  listSessionsMine,
  ensureFreshToken,
  startReadingCoach,
  answerReadingCoach,
  addSessionNote,
  getUploadLayout,
  deleteSession,
  startTutor,
  stepTutor,
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
  GraduationCap,
  CheckCircle,
  Table2,
  Trash2,
} from 'lucide-react'
// Lazy-load the PDF viewer so heavy pdf.js code is deferred until needed
const PdfViewer = React.lazy(() => import('@/components/PdfViewer.jsx'))

function EvidencePreview({ pdfUrl, page, bbox, kind }) {
  const canvasRef = useRef(null)
  const [state, setState] = useState('idle')

  useEffect(() => {
    let cancelled = false
    let loadingTask = null

    async function renderPreview() {
      if (!pdfUrl || !page || !canvasRef.current) return

      try {
        setState('loading')
        await initPdfWorker()
        const pdfjs = await import('pdfjs-dist')
        const token = getAuthToken()
        loadingTask = pdfjs.getDocument({
          url: pdfUrl,
          httpHeaders: token ? { Authorization: `Bearer ${token}` } : undefined,
          withCredentials: false,
        })

        const pdf = await loadingTask.promise
        if (cancelled) return

        const pdfPage = await pdf.getPage(page)
        if (cancelled) return

        const viewport = pdfPage.getViewport({ scale: 1.25 })
        const source = document.createElement('canvas')
        const sourceCtx = source.getContext('2d')
        source.width = Math.floor(viewport.width)
        source.height = Math.floor(viewport.height)
        await pdfPage.render({ canvasContext: sourceCtx, viewport }).promise
        if (cancelled) return

        const scale = viewport.width / pdfPage.getViewport({ scale: 1 }).width
        let sx = 0
        let sy = 0
        let sw = source.width
        let sh = source.height

        if (bbox && typeof bbox.left === 'number' && typeof bbox.top === 'number') {
          const pad = 24
          const pageHeightPt = pdfPage.getViewport({ scale: 1 }).height
          const boxLeft = bbox.left * scale
          const boxTop = (pageHeightPt - bbox.top) * scale
          const boxWidth = Math.max(1, (bbox.width || 1) * scale)
          const boxHeight = Math.max(1, (bbox.height || 1) * scale)

          if (kind === 'table') {
            sx = Math.max(0, boxLeft - pad)
            sy = Math.max(0, boxTop - pad)
            sw = Math.min(source.width - sx, boxWidth + pad * 2)
            sh = Math.min(source.height - sy, Math.max(boxHeight + pad * 2, 120))
          } else {
            sx = 0
            sw = source.width
            const figureHeight = Math.min(source.height * 0.45, 360)
            sy = Math.max(0, boxTop - figureHeight)
            sh = Math.min(source.height - sy, figureHeight + boxHeight + pad)
          }
        }

        const canvas = canvasRef.current
        const ctx = canvas.getContext('2d')
        const targetWidth = 260
        const targetHeight = Math.max(110, Math.round((sh / sw) * targetWidth))
        canvas.width = targetWidth
        canvas.height = targetHeight
        ctx.fillStyle = '#fff'
        ctx.fillRect(0, 0, targetWidth, targetHeight)
        ctx.drawImage(source, sx, sy, sw, sh, 0, 0, targetWidth, targetHeight)
        setState('ready')
      } catch (err) {
        console.debug('[EvidencePreview] render failed', err)
        if (!cancelled) setState('error')
      }
    }

    renderPreview()

    return () => {
      cancelled = true
      try {
        loadingTask?.destroy?.()
      } catch {
        /* ignore */
      }
    }
  }, [bbox, kind, page, pdfUrl])

  return (
    <div className="mb-3 overflow-hidden rounded border border-[#e4d6c7] bg-[#faf6f0]">
      <canvas ref={canvasRef} className="block w-full" aria-label="Detected visual preview" />
      {state === 'loading' && (
        <div className="px-3 py-2 text-xs text-muted-foreground">Rendering preview...</div>
      )}
      {state === 'error' && (
        <div className="px-3 py-2 text-xs text-muted-foreground">
          Preview unavailable. Open the page to inspect this evidence.
        </div>
      )}
    </div>
  )
}
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

  const [showNotes, setShowNotes] = useState(false)
  const [showHistory, setShowHistory] = useState(false)
  const [showFigures, setShowFigures] = useState(false)
  const [layoutEvidence, setLayoutEvidence] = useState([])
  const [layoutLoading, setLayoutLoading] = useState(false)
  const [layoutError, setLayoutError] = useState(null)
  const [messages, setMessages] = useState([
    {
      role: 'assistant',
      content: "Hello! I've analyzed your case study. What would you like to explore first?",
      createdAt: new Date().toISOString(),
    },
  ])
  const [message, setMessage] = useState('')
  const [readingStep, setReadingStep] = useState(null)
  const [pendingReadingStep, setPendingReadingStep] = useState(null)
  const [readingAnswer, setReadingAnswer] = useState('')
  const [readingLoading, setReadingLoading] = useState(false)
  const [readingError, setReadingError] = useState(null)
  const [guidedStep, setGuidedStep] = useState(null)
  const [guidedLoading, setGuidedLoading] = useState(false)
  const [guidedError, setGuidedError] = useState(null)
  const [notesRefreshKey, setNotesRefreshKey] = useState(0)
  // Use the local pdf controller state (`pdfCtrl`) which is provided below via PdfControllerProvider.
  // We avoid calling the context consumer here because the Provider is created later in this component.

  // Real conversation history from /sessions/mine
  const [conversationHistory, setConversationHistory] = useState([])
  const [_conversationLoading, setConversationLoading] = useState(false)
  const [conversationError, setConversationError] = useState('')
  const [deletingConversationId, setDeletingConversationId] = useState(null)

  // Active session (for this workspace)
  const [sessionId, setSessionId] = useState(() => {
    return searchParams.get('sessionId') || null
  })
  const [_sessionLoading, setSessionLoading] = useState(false)

  async function handleSendMessage() {
    const text = message.trim()
    if (!text) return

    if (uploadId) {
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
      setMessages(prev => [
        ...prev,
        { role: 'user', content: q, createdAt: new Date().toISOString() },
      ])
      setMessage('')
      const assistantId = `asst-${Date.now()}`
      setMessages(prev => [
        ...prev,
        {
          id: assistantId,
          role: 'assistant',
          content: '',
          streaming: true,
          sources: [],
          createdAt: new Date().toISOString(),
          wordsCount: 0,
        },
      ])

      if (indexState !== 'ready') {
        updateAssistantMessage(assistantId, {
          content: 'Preparing this document for Q&A...',
          streaming: true,
        })
        try {
          setIndexState('indexing')
          const summary = await buildIndex(uploadId)
          setIndexSummary(summary)
          setIndexState('ready')
        } catch (err) {
          console.error('[Workspace] failed to prepare document for chat', err)
          setIndexState('error')
          updateAssistantMessage(assistantId, {
            content: 'I could not prepare this document for Q&A. Please rebuild the index and try again.',
            streaming: false,
          })
          toast.error('Document indexing failed')
          return
        }
      }

      // use streaming ask endpoint with sessionId (if we have one)
      startAskStream(uploadId, q, assistantId, sid, {
        tutorSessionId: readingStep?.sessionId,
        tutorStepId: readingStep?.stepId,
      })
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
      {
        id: assistantId,
        role: 'assistant',
        content: '',
        streaming: true,
        sources: [],
        createdAt: new Date().toISOString(),
        wordsCount: 0,
      },
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
      updateAssistant(m => {
        const next = appendSmart(m.content || '', text)
        const wc = (next.match(/\S+/g) || []).length
        return { ...m, content: next, wordsCount: wc }
      })
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

  async function ensureWorkspaceSession() {
    if (sessionId) return sessionId
    if (!uploadId) return null

    const created = await createSession(uploadId)
    const sid = created?.sessionId || created?.id || null
    if (sid) setSessionId(sid)
    return sid
  }

  async function handleStartReadingCoach() {
    if (!uploadId) return

    setReadingLoading(true)
    setReadingError(null)
    try {
      if (indexState !== 'ready') {
        setIndexState('indexing')
        const summary = await buildIndex(uploadId)
        setIndexSummary(summary)
        setIndexState('ready')
      }

      const step = await startReadingCoach(uploadId)
      setReadingStep(step)
      setPendingReadingStep(null)
      setReadingAnswer('')
    } catch (err) {
      console.error('[Workspace] failed to start reading coach', err)
      setIndexState(prev => (prev === 'indexing' ? 'error' : prev))
      setReadingError(err?.message || 'Failed to start Reading Coach')
      toast.error('Reading Coach failed to start')
    } finally {
      setReadingLoading(false)
    }
  }

  async function handleStartGuidedMode() {
    if (!uploadId) return
    setGuidedLoading(true)
    setGuidedError(null)
    try {
      if (indexState !== 'ready') {
        setIndexState('indexing')
        const summary = await buildIndex(uploadId)
        setIndexSummary(summary)
        setIndexState('ready')
      }
      const sid = await ensureWorkspaceSession()
      const step = await startTutor(sid, uploadId)
      setGuidedStep(step)
    } catch (err) {
      console.error('[Workspace] failed to start guided mode', err)
      setGuidedError(err?.message || 'Guided Mode failed to start')
      toast.error('Guided Mode failed to start')
    } finally {
      setGuidedLoading(false)
    }
  }

  async function handleGuidedChoice(choiceId) {
    if (!guidedStep?.sessionId || !choiceId) return
    setGuidedLoading(true)
    setGuidedError(null)
    try {
      const next = await stepTutor(guidedStep.sessionId, choiceId)
      setGuidedStep(next)
    } catch (err) {
      console.error('[Workspace] guided mode step failed', err)
      setGuidedError(err?.message || 'Guided Mode step failed')
    } finally {
      setGuidedLoading(false)
    }
  }

  async function handleSubmitReadingAnswer() {
    const answer = readingAnswer.trim()
    if (!answer || !readingStep?.sessionId || !readingStep?.stepId) return

    setReadingLoading(true)
    setReadingError(null)
    try {
      const next = await answerReadingCoach(readingStep.sessionId, readingStep.stepId, answer)
      if (next?.feedback && next?.stage === 'retry') {
        setPendingReadingStep(null)
        setReadingStep({
          ...next,
          narrative: readingStep.narrative || next.narrative,
        })
        setReadingAnswer('')
      } else if (next?.feedback) {
        setPendingReadingStep(next)
        setReadingStep({
          ...readingStep,
          feedback: next.feedback,
          stage: 'feedback_pause',
        })
      } else {
        setPendingReadingStep(null)
        setReadingStep(next)
        setReadingAnswer('')
      }
    } catch (err) {
      console.error('[Workspace] failed to submit reading coach answer', err)
      setReadingError(err?.message || 'Failed to submit answer')
      toast.error('Reading Coach answer failed')
    } finally {
      setReadingLoading(false)
    }
  }

  function handleContinueReadingCoach() {
    if (!pendingReadingStep) return
    setReadingStep(pendingReadingStep)
    setPendingReadingStep(null)
    setReadingAnswer('')
  }

  function handleAskReadingCoachHelp() {
    if (!readingStep?.question) return
    setMessage(`I need help with this Reading Coach step: ${readingStep.question}`)
    requestAnimationFrame(() => scrollToBottom(true))
  }

  async function saveTextToNotes(text, successMessage = 'Saved to notes') {
    const content = (text || '').trim()
    if (!content) return

    try {
      const sid = await ensureWorkspaceSession()
      if (!sid) {
        toast.error('Open a document session before saving notes.')
        return
      }

      await addSessionNote(sid, content)
      setNotesRefreshKey(key => key + 1)
      toast.success(successMessage)
    } catch (err) {
      console.error('[Workspace] failed to save note', err)
      toast.error('Failed to save note')
    }
  }

  function formatReadingCoachNote() {
    if (!readingStep) return ''

    const lines = [
      `Reading Coach - ${readingStep.stepSummary || readingStep.stepId || 'Step'}`,
      '',
      readingStep.narrative || '',
    ]

    if (readingStep.question) {
      lines.push('', `Checkpoint: ${readingStep.question}`)
    }

    if (readingStep.feedback?.verdict) {
      lines.push('', `Feedback: ${readingStep.feedback.verdict}`)
    }

    if (readingStep.feedback?.hint) {
      lines.push(`Hint: ${readingStep.feedback.hint}`)
    }

    if (Array.isArray(readingStep.cites) && readingStep.cites.length > 0) {
      lines.push('', `Sources: ${readingStep.cites.map(page => `[p:${page}]`).join(' ')}`)
    }

    return lines.filter(line => line != null).join('\n')
  }

  function formatChatNote(msg) {
    const lines = ['Chat answer', '', msg?.content || '']
    if (Array.isArray(msg?.sources) && msg.sources.length > 0) {
      lines.push('', `Sources: ${msg.sources.map(source => `[p:${source.page}]`).join(' ')}`)
    }
    return lines.join('\n')
  }

  function updateAssistantMessage(assistantId, patch) {
    setMessages(prev => prev.map(m => (m.id === assistantId ? { ...m, ...patch } : m)))
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

  function normalizeLayoutEvidence(layout) {
    const captions = layout?.captions || layout?.Captions || []
    const tableCandidates = layout?.tables || layout?.Tables || []

    const normalizeBox = box => {
      if (!box) return null
      return {
        left: box.left ?? box.Left ?? 0,
        top: box.top ?? box.Top ?? 0,
        width: box.width ?? box.Width ?? 0,
        height: box.height ?? box.Height ?? 0,
      }
    }

    const captionItems = captions.map(item => {
      const kind = (item.kind || item.Kind || '').toLowerCase() === 'table' ? 'table' : 'figure'
      return {
        id: item.id || item.Id || `${kind}-${item.page || item.Page}-${item.label || item.Label}`,
        kind,
        label: item.label || item.Label || (kind === 'table' ? 'Table' : 'Figure'),
        page: item.page || item.Page || 1,
        text: item.text || item.Text || '',
        confidence: item.confidence ?? item.Confidence ?? null,
        reasons: item.reasons || item.Reasons || [],
        bbox: normalizeBox(item.bbox || item.BBox),
        source: 'caption',
      }
    })

    const candidateItems = tableCandidates.map(item => ({
      id: item.id || item.Id || `table-candidate-${item.page || item.Page}-${item.label || item.Label}`,
      kind: 'table',
      label: item.label || item.Label || 'Table candidate',
      page: item.page || item.Page || 1,
      text: item.textPreview || item.TextPreview || '',
      confidence: item.confidence ?? item.Confidence ?? null,
      reasons: item.reasons || item.Reasons || [],
      bbox: normalizeBox(item.bbox || item.BBox),
      source: 'candidate',
    }))

    const seen = new Set()
    return [...captionItems, ...candidateItems]
      .filter(item => {
        const key = `${item.kind}:${item.page}:${item.text}`
        if (seen.has(key)) return false
        seen.add(key)
        return true
      })
      .sort((a, b) => a.page - b.page || a.label.localeCompare(b.label))
  }

  useEffect(() => {
    if (!showFigures || !uploadId) return

    let cancelled = false
    async function loadLayoutEvidence() {
      setLayoutLoading(true)
      setLayoutError(null)
      try {
        const layout = await getUploadLayout(uploadId)
        if (!cancelled) {
          setLayoutEvidence(normalizeLayoutEvidence(layout))
        }
      } catch (err) {
        console.error('[Workspace] failed to load layout evidence', err)
        if (!cancelled) {
          setLayoutEvidence([])
          setLayoutError(err?.message || 'Failed to load figures and tables')
        }
      } finally {
        if (!cancelled) setLayoutLoading(false)
      }
    }

    loadLayoutEvidence()
    return () => {
      cancelled = true
    }
  }, [showFigures, uploadId])

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
      if (res.status === 404) {
        // The upload record may remain in PostgreSQL after its local artifact
        // was lost during an App Service redeploy. Do not start a rebuild for
        // a document that the API has already confirmed is missing.
        setIndexState('error')
        setIndexSummary(null)
        return
      }
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
  async function startAskStream(uploadIdParam, q, assistantId, sessionIdParam, tutorContext = {}) {
    // close any existing stream/abort controller
    try {
      if (sseRef.current?.abort) sseRef.current.abort()
    } catch {
      /* empty */
    }
    sseRef.current = null

    const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
    const params = new URLSearchParams({ q: q || '' })
    if (sessionIdParam) params.set('sessionId', sessionIdParam)
    if (tutorContext?.tutorSessionId) params.set('tutorSessionId', tutorContext.tutorSessionId)
    if (tutorContext?.tutorStepId) params.set('tutorStepId', tutorContext.tutorStepId)
    const url = API_BASE
      ? `${base}/ask/stream/${encodeURIComponent(uploadIdParam)}?${params.toString()}`
      : `/ask/stream/${encodeURIComponent(uploadIdParam)}?${params.toString()}`

    const controller = new AbortController()
    sseRef.current = controller

    const token = getAuthToken()
    const headers = token ? { Authorization: `Bearer ${token}` } : {}

    let gotFirstToken = false
    const updateAssistant = updater => {
      setMessages(prev => prev.map(m => (m.id === assistantId ? updater(m) : m)))
    }

    // Prevent starting a long-lived stream with an expired token. If the
    // token is expired, show a concise in-chat message prompting the user
    // to sign in rather than attempting the request which will 401.
    try {
      const fresh = ensureFreshToken()
      if (!fresh) {
        updateAssistant(m => ({
          ...m,
          streaming: false,
          content: m.content
            ? `${m.content}\n\nSession expired — please sign in to continue.`
            : 'Session expired — please sign in to continue.',
        }))
        toast.error('Session expired — please sign in to continue')
        if (sseRef.current === controller) sseRef.current = null
        return
      }
    } catch {
      // If token parsing failed for any reason, fail safe and prompt
      updateAssistant(m => ({ ...m, streaming: false, content: 'Session expired — please sign in to continue.' }))
      toast.error('Session expired — please sign in to continue')
      if (sseRef.current === controller) sseRef.current = null
      return
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
              updateAssistant(m => {
                const next = appendSmart(m.content || '', piece)
                const wc = (next.match(/\S+/g) || []).length
                return { ...m, content: next, wordsCount: wc }
              })
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
              const wc = (cleaned.match(/\S+/g) || []).length
              return { ...m, content: cleaned, streaming: false, wordsCount: wc }
            })

            // No need to abort here; stream will naturally finish.
            if (sseRef.current === controller) sseRef.current = null
            requestAnimationFrame(() => scrollToBottom(true))
          } else if (event === 'error') {
            let parsed = null
            try {
              parsed = JSON.parse(data)
            } catch {
              parsed = { message: data }
            }
            const message = parsed?.message || parsed?.error || 'The assistant could not answer this request.'
            updateAssistant(m => ({
              ...m,
              content: m.content ? `${m.content}\n\n${message}` : message,
              streaming: false,
            }))
            toast.error(message)
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
              startAskStream(uploadIdParam, q, assistantId, sessionIdParam, tutorContext)
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

  async function handleDeleteConversation(conversation, event) {
    event?.stopPropagation()
    if (!conversation?.id) return

    const confirmed = window.confirm(
      'Delete this conversation and its notes? This cannot be undone.'
    )
    if (!confirmed) return

    try {
      setDeletingConversationId(conversation.id)
      await deleteSession(conversation.id)
      setConversationHistory(prev => prev.filter(item => item.id !== conversation.id))

      if (sessionId === conversation.id) {
        setSessionId(null)
        setMessages([
          {
            role: 'assistant',
            content:
              "Hello! I've analyzed your case study. What would you like to explore first?",
          },
        ])
        navigate(`/workspace/${encodeURIComponent(uploadId || conversation.caseId || '')}`, {
          replace: true,
        })
      }

      toast.success('Conversation deleted')
    } catch (err) {
      console.error('[Workspace] failed to delete conversation', err)
      toast.error(err?.message || 'Failed to delete conversation')
    } finally {
      setDeletingConversationId(null)
    }
  }

  // Load conversation list for the left "Conversation History" panel
  useEffect(() => {
    let cancelled = false

    async function loadConversations() {
      try {
        setConversationLoading(true)
        setConversationError('')
        const sessions = await listSessionsMine()

        if (cancelled) return

        const sessionItems = getPagedItems(sessions)
        const mapped = sessionItems.map(s => {
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

        setConversationHistory(mapped)
      } catch (err) {
        console.error('[Workspace] failed to load conversation history', err)
        if (!cancelled) setConversationError(err?.message || 'Failed to load conversation history')
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

        const historyItems = getPagedItems(history)

        if (historyItems.length > 0) {
          setMessages(
            historyItems.map(m => ({
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
  const [pdfLoadError, setPdfLoadError] = useState(null)
  const [uploadDate, setUploadDate] = useState(null)
  const [indexState, setIndexState] = useState('not-indexed') // 'not-indexed' | 'indexing' | 'ready' | 'error'
  const [indexSummary, setIndexSummary] = useState(null)
  const sseRef = useRef(null)
  const retriedRef = useRef(false)

  const chatRef = useRef(null)

  const historyCardBase =
    'group p-3 rounded-2xl border border-[#e4d6c7]/60 bg-white shadow-sm transition-colors duration-150 cursor-pointer hover:bg-[#faf6f0]'
  const historyCardActive =
    'border-l-[3px] border-[#c96a0a] bg-[#f6eee5] hover:bg-[#f6eee5]'
  const getHistoryCardClass = isActive => `${historyCardBase} ${isActive ? historyCardActive : ''}`
  const tabButtonBase =
    'inline-flex items-center gap-2 rounded-t-full px-4 py-2 text-xs font-semibold transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary focus-visible:ring focus-visible:ring-ring focus-visible:ring-offset-1 hover:bg-[#faf6f0] cursor-pointer'
  const getTabClass = isActive => `${tabButtonBase} ${isActive ? 'text-foreground' : 'text-muted-foreground'}`
  const getTabStyle = isActive => ({
    backgroundColor: isActive ? '#f6eee5' : 'transparent',
    borderBottomWidth: isActive ? 3 : 0,
    borderBottomStyle: 'solid',
    borderBottomColor: '#c96a0a',
  })

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
          className="min-h-screen bg-[#faf6f0] flex flex-col"
        data-mode="chat"
        data-shownotes={String(showNotes)}
        data-showhistory={String(showHistory)}
        data-showfigures={String(showFigures)}
        data-uploadid={uploadId || ''}
        data-casetype={caseType}
      >
        {/* Header */}
          <header className="sticky top-0 z-20 h-14 border-b border-[#e4d6c7] bg-card/50 backdrop-blur-sm flex-shrink-0">
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
                  'inline-flex items-center justify-center rounded-md text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:opacity-50 disabled:pointer-events-none hover:bg-[#f6eee5] cursor-pointer h-9 px-3 gap-2'
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
              <div className="hidden md:flex items-center gap-1 rounded-full bg-[#f6f0e8]/80 px-1 py-1">
                <button
                  type="button"
                  className={getTabClass(!showNotes)}
                  style={getTabStyle(!showNotes)}
                  onClick={() => setShowNotes(false)}
                  aria-pressed={!showNotes}
                >
                  <MessageSquare className="w-3 h-3" />
                  <span>Chat</span>
                </button>
                <button
                  type="button"
                  className={getTabClass(showNotes)}
                  style={getTabStyle(showNotes)}
                  onClick={() => setShowNotes(true)}
                  aria-pressed={showNotes}
                >
                  <StickyNote className="w-3 h-3" />
                  <span>Notes</span>
                </button>
              </div>

              <Button
                variant="outline"
                size="sm"
                className="gap-2 bg-transparent md:hidden hover:text-[#c96a0a]"
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
        {/* Demo banner removed */}

        <div aria-hidden={showHistory || showFigures || showNotes ? 'true' : 'false'}>
          {/* On lg+ we bound the content area to the viewport height minus the header
              so the page itself doesn't scroll; only the chat panel is scrollable.
              Assumption: header is h-14 (3.5rem). If header height changes, replace
              the hard calc with a CSS variable. */}
          <div className="flex-1 flex flex-col md:flex-row overflow-hidden lg:h-[calc(100vh-3.5rem)] divide-y divide-[#E8DDD0] md:divide-y-0 md:divide-x">
      {/* Permanent sidebar on md+; falls back to drawer on small screens */}
      {/* On large screens keep the history panel visually fixed (no internal scroll).
        On smaller screens allow overflow so the drawer can scroll. */}
  <aside className="hidden lg:flex lg:flex-col lg:flex-[0_0_20%] lg:min-w-0 border-r border-[#e4d6c7] bg-white shadow-sm lg:sticky lg:top-14 lg:h-[calc(100vh-3.5rem)] lg:overflow-hidden">
              <div className="flex flex-col flex-1 overflow-hidden">
                <div className="sticky top-0 z-10 border-b border-[#E8DDD0] bg-white px-4 py-4">
                  <div className="flex items-center justify-between">
                    <h3 className="font-semibold text-foreground">Conversation History</h3>
                  </div>
                </div>
                <div className="flex-1 overflow-auto p-4 space-y-2">
                  {conversationError && (
                    <div className="rounded-md border border-red-200 bg-red-50 p-3 text-xs text-red-700">
                      <div className="font-medium">Could not load conversations.</div>
                      <div className="mt-1 text-red-600">Refresh the page or sign in again.</div>
                    </div>
                  )}
                  {!conversationError && conversationHistory.length === 0 && (
                    <div className="rounded-md border border-[#E8DDD0] bg-[#faf6f0] p-3 text-xs text-[#5C4C3C]">
                      No conversations yet.
                    </div>
                  )}
                  {conversationHistory.map(c => {
                    const isActive = sessionId === c.id
                    return (
                      <Card
                        key={c.id}
                        className={getHistoryCardClass(isActive)}
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
                            <h4 className="text-sm font-semibold text-foreground/90 line-clamp-1">
                              {c.title}
                            </h4>
                            <div className="flex items-center gap-1">
                              <span className="text-xs text-muted-foreground/80 whitespace-nowrap">
                                {c.messageCount}
                              </span>
                              <button
                                type="button"
                                className="rounded p-1 text-muted-foreground/60 transition hover:bg-red-50 hover:text-red-600"
                                aria-label="Delete conversation"
                                disabled={deletingConversationId === c.id}
                                onClick={event => handleDeleteConversation(c, event)}
                              >
                                <Trash2 className="h-3.5 w-3.5" />
                              </button>
                            </div>
                          </div>
                          <p className="text-xs text-muted-foreground/70 line-clamp-2">
                            {c.preview}
                          </p>
                          <div className="flex items-center gap-1 text-xs text-muted-foreground/70">
                            <Clock className="w-3 h-3" />
                            {c.date}
                          </div>
                        </div>
                      </Card>
                    )
                  })}
                </div>
              </div>
            </aside>

      {/* Center PDF area: allow normal vertical scrolling on small screens
        but keep fixed (no internal scroll) on large screens so only the
        chat panel scrolls. */}
      <div className="flex-1 min-w-0 border-r border-[#e4d6c7] bg-[#faf6f0] overflow-hidden lg:flex-[0_0_45%] lg:sticky lg:top-14 lg:h-[calc(100vh-3.5rem)]">
              <div className="p-6 max-w-3xl mx-auto">
                <Card className="bg-card">
                  <div className="p-4">
                    {uploadId ? (
                      <div className="w-full h-[70vh] lg:h-[calc(100vh-3.5rem)] bg-white rounded overflow-hidden border border-[#e4d6c7] relative shadow-sm">
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
                            onError={err => {
                              // capture structured PDF fetch errors (eg. 401) so parent can show UX
                              try {
                                setPdfLoadError(err || null)
                              } catch {
                                /* ignore */
                              }
                            }}
                            onReady={ctrl => {
                              pdfCtrlRef.current = ctrl
                              setPdfCtrl(ctrl) // <-- context value
                              if (import.meta.env.DEV) window.pdfCtrl = ctrl
                            }}
                          />
                          {pdfLoadError && (
                            <div className="absolute inset-0 z-30 flex items-start justify-center p-6 pointer-events-none">
                              <div className="bg-amber-50 border border-amber-200 text-amber-900 text-sm rounded p-3 pointer-events-auto shadow">
                                <div className="space-y-2">
                                  <div className="font-medium">Failed to load PDF</div>
                                  <div className="text-xs text-foreground/80">
                                    {pdfLoadError?.status === 401
                                      ? 'This document requires authentication. Please sign in to view it.'
                                      : pdfLoadError?.message || 'An unexpected error occurred while loading the PDF.'}
                                  </div>
                                  <div className="pt-2 flex items-center gap-2">
                                    <Link to="/signin">
                                      <Button size="sm">Sign in</Button>
                                    </Link>
                                    <Link to="/signup">
                                      <Button size="sm" variant="outline">
                                        Sign up
                                      </Button>
                                    </Link>
                                  </div>
                                </div>
                              </div>
                            </div>
                          )}
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
                        <div className="flex items-center justify-between pb-4 border-b border-[#e4d6c7]">
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

            <div className="w-full md:flex-1 lg:flex-[0_0_35%] lg:min-w-[24rem] flex-shrink-0 flex flex-col bg-white border border-[#e4d6c7] shadow-sm">
              <div className="border-b border-[#e4d6c7] bg-[#fffaf4] p-4">
                {!guidedStep ? (
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <div className="text-sm font-semibold text-[#2c2218]">Guided Analysis</div>
                      <p className="mt-1 text-xs text-[#7a5c3e]">
                        Follow a structured path through the document with evidence-grounded choices.
                      </p>
                    </div>
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={handleStartGuidedMode}
                      disabled={!uploadId || guidedLoading}
                    >
                      {guidedLoading ? 'Starting…' : 'Start'}
                    </Button>
                  </div>
                ) : (
                  <GuidedModeFlow
                    tutorStep={guidedStep}
                    onChoice={handleGuidedChoice}
                    isLoading={guidedLoading}
                    loadingChoiceId={guidedLoading ? guidedStep?.lastChoiceId : null}
                    activePathTitle={guidedStep?.focus || guidedStep?.pathTitle || 'Guided Analysis'}
                    onResetPath={handleStartGuidedMode}
                  />
                )}
                {guidedError && <p className="mt-2 text-xs text-red-700">{guidedError}</p>}
              </div>
              <div className="flex-1 flex flex-col overflow-hidden">
                {/* Messages */}
                <div
                  ref={chatRef}
                  className={`flex-1 overflow-auto p-4 space-y-4 pb-20 ${showFigures ? 'pr-64' : ''}`}
                >
                  <Card className="border-[#e4d6c7] bg-[#fffaf4] p-4 shadow-sm">
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <div className="flex items-center gap-2 text-sm font-semibold text-[#2c2218]">
                          <GraduationCap className="h-4 w-4 text-[#B86A17]" />
                          Reading Coach
                        </div>
                        <p className="mt-1 text-xs leading-relaxed text-[#7a5c3e]">
                          Work through the paper step by step, answer short checks, and get feedback
                          before moving on.
                        </p>
                      </div>
                      <Button
                        size="sm"
                        variant="warm"
                        onClick={handleStartReadingCoach}
                        disabled={!uploadId || readingLoading}
                        className="shrink-0"
                      >
                        {readingLoading
                          ? indexState === 'indexing'
                            ? 'Preparing...'
                            : 'Starting...'
                          : readingStep
                            ? 'Restart'
                            : 'Start'}
                      </Button>
                    </div>

                    {readingError && (
                      <p className="mt-3 rounded border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
                        {readingError}
                      </p>
                    )}

                    {readingStep && (
                      <div className="mt-4 space-y-3 border-t border-[#ead8c7] pt-4">
                        <div className="flex items-center justify-between gap-3 text-xs text-[#7a5c3e]">
                          <span>
                            Step {readingStep.stepNumber || 1} of {readingStep.totalSteps || 6}
                          </span>
                          <span className="rounded-full bg-white px-2 py-1 font-medium text-[#6A3A0A]">
                            {readingStep.stage === 'recap'
                              ? 'Recap'
                              : readingStep.stage === 'feedback_pause'
                                ? 'Feedback'
                                : 'Check'}
                          </span>
                        </div>

                        {readingStep.feedback && (
                          <div className="rounded border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs text-emerald-900">
                            <div className="flex items-center gap-2 font-semibold">
                              <CheckCircle className="h-3.5 w-3.5" />
                              Feedback
                            </div>
                            <p className="mt-1">{readingStep.feedback.verdict}</p>
                            {readingStep.feedback.hint && (
                              <p className="mt-1 text-emerald-800">Coaching note: {readingStep.feedback.hint}</p>
                            )}
                          </div>
                        )}

                        <div className="whitespace-pre-wrap text-sm leading-relaxed text-[#2c2218]">
                          {readingStep.narrative}
                        </div>

                        {Array.isArray(readingStep.cites) && readingStep.cites.length > 0 && (
                          <div className="flex flex-wrap gap-2">
                            {readingStep.cites.slice(0, 4).map(page => (
                              <button
                                key={page}
                                type="button"
                                disabled={!pdfCtrl}
                                onClick={() => pdfCtrl?.showHighlight({ page })}
                                className="rounded-full border border-[#E4C6A1] bg-white px-2 py-1 text-xs text-[#6A3A0A] transition-colors hover:bg-[#F6EEE5] disabled:cursor-not-allowed disabled:opacity-50"
                              >
                                p.{page}
                              </button>
                            ))}
                          </div>
                        )}

                        {readingStep.stage === 'feedback_pause' && (
                          <div className="space-y-3 rounded-md border border-[#E4C6A1] bg-white px-3 py-3">
                            <div>
                              <p className="text-sm font-medium text-[#2c2218]">
                                {readingStep.question}
                              </p>
                              {readingAnswer.trim() ? (
                                <p className="mt-2 rounded bg-[#faf6f0] px-3 py-2 text-sm text-[#5C4C3C]">
                                  {readingAnswer.trim()}
                                </p>
                              ) : null}
                            </div>
                            <Button
                              size="sm"
                              type="button"
                              onClick={handleContinueReadingCoach}
                              disabled={!pendingReadingStep}
                              className="bg-[#C96A0A] text-white hover:bg-[#B85F0A]"
                            >
                              Continue
                            </Button>
                          </div>
                        )}

                        {readingStep.stage !== 'recap' && readingStep.stage !== 'feedback_pause' && readingStep.question && (
                          <div className="space-y-2">
                            <p className="text-sm font-medium text-[#2c2218]">
                              {readingStep.question}
                            </p>
                            <Textarea
                              value={readingAnswer}
                              onChange={e => setReadingAnswer(e.target.value)}
                              placeholder="Answer in your own words..."
                              className="min-h-[84px] bg-white text-sm"
                            />
                            <Button
                              size="sm"
                              onClick={handleSubmitReadingAnswer}
                              disabled={readingLoading || !readingAnswer.trim()}
                              className="bg-[#C96A0A] text-white hover:bg-[#B85F0A]"
                            >
                              {readingLoading ? 'Checking...' : 'Submit answer'}
                            </Button>
                            <Button
                              size="sm"
                              variant="outline"
                              type="button"
                              onClick={handleAskReadingCoachHelp}
                              disabled={!readingStep?.question}
                              className="ml-2 border-[#E4C6A1] text-[#6A3A0A] hover:bg-[#F6EEE5]"
                            >
                              Ask for help
                            </Button>
                            <Button
                              size="sm"
                              variant="outline"
                              type="button"
                              onClick={() =>
                                saveTextToNotes(formatReadingCoachNote(), 'Reading Coach saved to notes')
                              }
                              disabled={!readingStep?.narrative}
                              className="ml-2 border-[#E4C6A1] text-[#6A3A0A] hover:bg-[#F6EEE5]"
                            >
                              Save to notes
                            </Button>
                          </div>
                        )}
                        {readingStep.stage === 'recap' && (
                          <Button
                            size="sm"
                            variant="outline"
                            type="button"
                            onClick={() =>
                              saveTextToNotes(formatReadingCoachNote(), 'Recap saved to notes')
                            }
                            disabled={!readingStep?.narrative}
                            className="border-[#E4C6A1] text-[#6A3A0A] hover:bg-[#F6EEE5]"
                          >
                            Save recap to notes
                          </Button>
                        )}
                      </div>
                    )}
                  </Card>

                  {messages.map((msg, idx) => {
                    const isUser = msg.role === 'user'
                    return (
                      <div
                        key={msg.id ?? idx}
                        className={`flex ${isUser ? 'justify-end' : 'justify-start'}`}
                      >
                            <div
                              className={`max-w-[85%] rounded-lg p-3 transition-colors duration-150 ${
                                isUser
                                  ? 'bg-[#B86A17] text-white hover:bg-[#A65408]'
                                  : 'bg-white text-foreground'
                              }`}
                            >
                              <div className="text-sm leading-relaxed whitespace-pre-wrap break-words">
                                {msg.streaming && !(msg.content || '').trim() ? (
                                  // show skeleton until first token arrives
                                  <div className="skeleton" style={{ width: '9rem', height: '1.1rem' }} aria-hidden="true" />
                                ) : (
                                  <span className="message-text">{msg.content}</span>
                                )}

                                {msg.streaming && (
                                  <span className="ml-2" aria-hidden="true">
                                    <span className="typing-indicator" aria-hidden="true">
                                      <span className="typing-dots" aria-hidden="true">
                                        <span />
                                        <span />
                                        <span />
                                      </span>
                                      <span className="typing-label">Assistant is typing…</span>
                                    </span>
                                  </span>
                                )}
                              </div>

                              {/* ↓ Source chip(s) */}
                              {!isUser && msg.sources && msg.sources.length > 0 && (
                                <div className="mt-2 flex gap-2">
                                  {msg.sources.map((s, i) => (
                                    <button
                                      key={i}
                                      type="button"
                                      disabled={!pdfCtrl}
                                      onClick={() => pdfCtrl?.showHighlight({ page: s.page })}
                                      className="text-xs px-2 py-1 rounded-full border border-[#E4C6A1] bg-[#F6EEE5] text-[#6A3A0A] transition-colors hover:bg-[#EFE2D4] cursor-pointer disabled:cursor-not-allowed disabled:opacity-50"
                                      title={`Open ${s.label}`}
                                    >
                                      {s.label}
                                    </button>
                                  ))}
                                </div>
                              )}

                              {/* message meta: small progress (words) */}
                              <div className="message-meta">
                                {msg.streaming && (
                                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                                    <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
                                      {msg.wordsCount ?? 0} words
                                    </div>
                                    <div className="progress-bar" aria-hidden="true">
                                      <i style={{ width: `${Math.min(100, (msg.wordsCount || 0) / 2)}%` }} />
                                    </div>
                                  </div>
                                )}
                                {!isUser && !msg.streaming && (msg.content || '').trim() && (
                                  <button
                                    type="button"
                                    onClick={() =>
                                      saveTextToNotes(formatChatNote(msg), 'Chat answer saved to notes')
                                    }
                                    className="mt-2 text-xs font-medium text-[#6A3A0A] hover:text-[#C96A0A]"
                                  >
                                    Save to notes
                                  </button>
                                )}
                              </div>
                            </div>
                      </div>
                    )
                  })}

                  {/* Suggested Questions - hidden on mobile, visible md+ */}
                  <div className="pt-4 hidden md:block">
                    <p className="text-xs text-muted-foreground flex items-center gap-1 font-semibold tracking-wide">
                      <Lightbulb className="w-3 h-3" />
                      Suggested Questions
                    </p>
                    <div className="mt-3 flex flex-wrap gap-2">
                      {[
                        'What is the main problem?',
                        'What evidence supports this?',
                        'What are potential solutions?',
                      ].map((q, i) => (
                        <button
                          key={i}
                          type="button"
                          className="rounded-full border border-[#e4c6a1] bg-[#faf6f0] px-3 py-1.5 text-xs font-normal text-foreground transition-colors hover:border-[#c96a0a] hover:bg-[#f6eee5] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#c96a0a] cursor-pointer"
                          onClick={() => setMessage(q)}
                        >
                          {q}
                        </button>
                      ))}
                    </div>
                  </div>
                </div>

                {/* Input */}
                <div className="border-t border-[#e4d6c7] p-4 bg-white sticky bottom-0 z-20">
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
                          // Enter sends; Shift+Enter keeps the newline.
                        }}
                        className="w-full min-h-[56px] max-h-[140px] resize-none px-4 py-3 leading-relaxed text-sm focus-visible:border-[#C96A0A] focus-visible:shadow-[0_0_0_2px_rgba(201,106,10,0.15)] focus-visible:outline-none"
                      />
                    </div>

                    <div className="flex-shrink-0 self-start -translate-y-1">
                      <Button
                        size="icon"
                        onClick={handleSendMessage}
                        aria-label="Send message"
                        className="h-10 w-10 rounded-full bg-[#C96A0A] text-white shadow-sm transition-colors duration-150 hover:bg-[#B85F0A] active:bg-[#A65408] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:ring-offset-[#fdf9f4] focus-visible:ring-[#C96A0A]/60 disabled:opacity-70 disabled:cursor-not-allowed"
                      >
                        <Send className="w-4 h-4" />
                      </Button>
                    </div>
                  </div>
                </div>
              </div>
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
            <div className="fixed top-14 bottom-0 left-0 w-80 bg-white border-r border-[#e4d6c7] shadow-xl z-50 overflow-auto lg:hidden">
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
                  {conversationError && (
                    <div className="rounded-md border border-red-200 bg-red-50 p-3 text-xs text-red-700">
                      <div className="font-medium">Could not load conversations.</div>
                      <div className="mt-1 text-red-600">Refresh the page or sign in again.</div>
                    </div>
                  )}
                  {!conversationError && conversationHistory.length === 0 && (
                    <div className="rounded-md border border-[#E8DDD0] bg-[#faf6f0] p-3 text-xs text-[#5C4C3C]">
                      No conversations yet.
                    </div>
                  )}
                  {conversationHistory.map(c => {
                    const isActive = sessionId === c.id
                    return (
                      <Card
                        key={c.id}
                        className={getHistoryCardClass(isActive)}
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
                            <h4 className="text-sm font-semibold text-foreground/90 line-clamp-1">
                              {c.title}
                            </h4>
                            <div className="flex items-center gap-1">
                              <span className="text-xs text-muted-foreground/80 whitespace-nowrap">
                                {c.messageCount}
                              </span>
                              <button
                                type="button"
                                className="rounded p-1 text-muted-foreground/60 transition hover:bg-red-50 hover:text-red-600"
                                aria-label="Delete conversation"
                                disabled={deletingConversationId === c.id}
                                onClick={event => handleDeleteConversation(c, event)}
                              >
                                <Trash2 className="h-3.5 w-3.5" />
                              </button>
                            </div>
                          </div>
                          <p className="text-xs text-muted-foreground/70 line-clamp-2">
                            {c.preview}
                          </p>
                          <div className="flex items-center gap-1 text-xs text-muted-foreground/70">
                            <Clock className="w-3 h-3" />
                            {c.date}
                          </div>
                        </div>
                      </Card>
                    )
                  })}
                </div>
              </div>
            </div>
          </>
        )}

        {showFigures && (
          <div className="fixed top-0 bottom-0 right-0 w-full max-w-sm bg-white border-l border-[#e4d6c7] shadow-xl z-60 overflow-auto">
            <div className="p-4 space-y-4">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <h3 className="font-semibold text-foreground">Figures & Tables</h3>
                  <p className="text-xs text-muted-foreground">
                    Evidence detected from the document layout.
                  </p>
                </div>
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
                {layoutLoading ? (
                  <Card className="p-4 text-sm text-muted-foreground">
                    Scanning layout evidence...
                  </Card>
                ) : layoutError ? (
                  <Card className="p-4 text-sm text-red-700 bg-red-50 border-red-200">
                    {layoutError}
                  </Card>
                ) : layoutEvidence.length === 0 ? (
                  <Card className="p-4 text-sm text-muted-foreground">
                    No figures or tables were detected in this document yet.
                  </Card>
                ) : (
                  layoutEvidence.map(item => {
                    const disabled = !pdfCtrl
                    const previewPdfUrl = API_BASE
                      ? `${String(API_BASE).replace(/\/$/, '')}/uploads/${uploadId}.pdf`
                      : `/uploads/${uploadId}.pdf`
                    const confidence =
                      typeof item.confidence === 'number'
                        ? `${Math.round(item.confidence * 100)}%`
                        : null
                    const hasRegion =
                      item.bbox &&
                      typeof item.bbox.width === 'number' &&
                      typeof item.bbox.height === 'number'
                    return (
                      <Card
                        key={item.id}
                        className={`p-3 cursor-pointer hover:border-primary/50 transition-colors ${disabled ? 'opacity-50 cursor-not-allowed' : ''}`}
                        onClick={() => {
                          if (!pdfCtrl) return
                          pdfCtrl.showHighlight({ page: item.page })
                        }}
                        aria-disabled={disabled}
                        role="button"
                        tabIndex={disabled ? -1 : 0}
                        onKeyDown={event => {
                          if (disabled) return
                          if (event.key === 'Enter' || event.key === ' ') {
                            event.preventDefault()
                            pdfCtrl?.showHighlight({ page: item.page })
                          }
                        }}
                      >
                        <EvidencePreview
                          pdfUrl={previewPdfUrl}
                          page={item.page}
                          bbox={item.bbox}
                          kind={item.kind}
                        />
                        <div className="mb-3 flex items-start gap-3">
                          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded border border-[#E4C6A1] bg-[#F6EEE5] text-[#6A3A0A]">
                            {item.kind === 'table' ? (
                              <Table2 className="h-4 w-4" />
                            ) : (
                              <ImageIcon className="h-4 w-4" />
                            )}
                          </div>
                          <div className="min-w-0 flex-1">
                            <div className="flex flex-wrap items-center gap-2">
                              <p className="text-sm font-semibold text-foreground">{item.label}</p>
                              {item.source === 'candidate' && (
                                <span className="rounded-full bg-amber-50 px-2 py-0.5 text-[11px] text-amber-800 border border-amber-200">
                                  detected
                                </span>
                              )}
                            </div>
                            <p className="text-xs text-muted-foreground">
                              Page {item.page}
                              {confidence ? ` - confidence ${confidence}` : ''}
                              {hasRegion ? ' - page region detected' : ''}
                            </p>
                          </div>
                        </div>

                        <p className="text-xs leading-relaxed text-foreground/90">
                          {item.text || 'No caption text available.'}
                        </p>

                        {Array.isArray(item.reasons) && item.reasons.length > 0 && (
                          <div className="mt-2 flex flex-wrap gap-1">
                            {item.reasons.slice(0, 3).map(reason => (
                              <span
                                key={reason}
                                className="rounded-full bg-slate-50 px-2 py-0.5 text-[10px] text-slate-600 border border-slate-200"
                              >
                                {String(reason).replaceAll('_', ' ')}
                              </span>
                            ))}
                          </div>
                        )}
                      </Card>
                    )
                  })
                )}
              </div>
            </div>
          </div>
        )}
        <WorkspaceNotesPanel
          open={showNotes}
          onOpenChange={setShowNotes}
          currentCaseId={uploadId}
          currentSessionId={sessionId || 'session-current'}
          refreshKey={notesRefreshKey}
          panelRef={notesPanelRef}
        />
      </div>
    </PdfControllerProvider>
  )
}
