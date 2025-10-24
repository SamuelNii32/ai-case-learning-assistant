import { Link, useParams, useSearchParams } from 'react-router-dom'
import { useState, useEffect, useRef } from 'react'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import Badge from '@/components/ui/badge'
import { Textarea } from '@/components/ui/textarea'
import WorkspaceNotesPanel from '@/components/WorkspaceNotesPanel.clean'
import GuidedModePanel from '@/components/GuidedModePanel.clean'
import { PdfControllerProvider } from '@/contexts/pdf-controller'

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
import { API_BASE } from '@/config'
// Explicitly import the JSX viewer implementation (prefer stable JS build)
import PdfViewer from '@/components/PdfViewer.jsx'

export default function Workspace() {
  const { uploadId } = useParams()

  const [searchParams] = useSearchParams()
  const caseType = searchParams.get('type') || 'personal'

  const [mode, setMode] = useState('chat')
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

  const conversationHistory = [
    {
      id: 1,
      title: 'Healthcare Innovation Case',
      date: '2 hours ago',
      preview: 'Discussed triage processes and bed availability…',
      messageCount: 12,
    },
    {
      id: 2,
      title: 'Supply Chain Analysis',
      date: 'Yesterday',
      preview: 'Analyzed inventory management and vendor relations…',
      messageCount: 8,
    },
    {
      id: 3,
      title: 'Marketing Strategy Case',
      date: '2 days ago',
      preview: 'Explored digital transformation and engagement…',
      messageCount: 15,
    },
    {
      id: 4,
      title: 'Financial Performance Review',
      date: '3 days ago',
      preview: 'Revenue streams and cost optimization…',
      messageCount: 10,
    },
  ]

  function handleSendMessage() {
    const text = message.trim()
    if (!text) return

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
        content: m.content ? `${m.content} ${text}` : text,
      }))
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
  const [pdfReady, setPdfReady] = useState(false)
  const sseRef = useRef(null)

  useEffect(() => {
    setPdfReady(false)
    pdfCtrlRef.current = null
  }, [uploadId])

  useEffect(() => {
    return () => {
      try {
        sseRef.current?.close()
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
        data-mode={mode}
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
                <span className="text-sm font-medium text-foreground truncate">
                  Healthcare Innovation Case
                </span>
                {(searchParams.get('type') || 'personal') === 'assigned' && (
                  <Badge variant="secondary" className="ml-2">
                    Assigned
                  </Badge>
                )}
              </div>
            </div>

            <div className="flex items-center gap-2 flex-shrink-0">
              <div className="hidden md:flex items-center gap-1 bg-muted rounded-lg p-1">
                <Button
                  variant={mode === 'chat' ? 'secondary' : 'ghost'}
                  size="sm"
                  onClick={() => setMode('chat')}
                  className="text-xs"
                >
                  <MessageSquare className="w-3 h-3 mr-1" />
                  Chat
                </Button>
                <Button
                  variant={mode === 'guided' ? 'secondary' : 'ghost'}
                  size="sm"
                  onClick={() => setMode('guided')}
                  className="text-xs"
                >
                  <Sparkles className="w-3 h-3 mr-1" />
                  Guided
                </Button>
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

              {import.meta.env.DEV && (
                <div className="hidden md:flex items-center gap-2">
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={!pdfReady}
                    onClick={() => pdfCtrlRef.current?.scrollToPage(5)}
                  >
                    Jump to 5
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={!pdfReady}
                    onClick={() => pdfCtrlRef.current?.showHighlight({ page: 5 })}
                  >
                    Flash 5
                  </Button>
                </div>
              )}
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
                      onClick={() => setShowHistory(false)}
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
                        <PdfViewer
                          src={`${API_BASE}/uploads/${uploadId}.pdf`}
                          unmirror
                          fitToWidth
                          onReady={ctrl => {
                            pdfCtrlRef.current = ctrl
                            setPdfCtrl(ctrl) // <-- context value
                            setPdfReady(true)
                            if (import.meta.env.DEV) {
                              window.pdfCtrl = ctrl
                              console.log('[Workspace] pdfCtrl ready:', ctrl)
                            }
                          }}
                        />
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
                            Healthcare Innovation Case Study
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
              {mode === 'chat' ? (
                <>
                  {/* Messages */}
                  <div
                    className={`flex-1 overflow-auto p-4 space-y-4 ${showFigures ? 'pr-64' : ''}`}
                  >
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
                            <p className="text-sm leading-relaxed">
                              {msg.content}
                              {msg.streaming && <span className="animate-pulse ml-1">▎</span>}
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
              ) : (
                <GuidedModePanel />
              )}
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
                      onClick={() => setShowHistory(false)}
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
          currentSessionId="session-current"
          panelRef={notesPanelRef}
        />
      </div>
    </PdfControllerProvider>
  )
}
