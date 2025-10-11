import { useState, useRef, useEffect } from "react"
import { useSearchParams } from "react-router-dom"
import { Calendar, Clock, MoreVertical, Search, Share, Edit, Archive, Trash2, FileText } from "lucide-react"

const mockSessions = [
  {
    id: 101,
    caseId: 1,
    caseTitle: "Supply Chain Disruption Analysis",
    mode: "Guided",
    startedAt: "2025-10-05T10:00:00Z",
    endedAt: "2025-10-05T12:15:00Z",
    date: "Oct 5, 2025",
    time: "10:00 AM",
    duration: "2h 15m",
    status: "completed",
    lastNoteAt: "2025-10-05T10:35:00Z",
    hasNotes: true,
  },
  {
    id: 202,
    caseId: 2,
    caseTitle: "Market Entry Strategy", 
    mode: "Free",
    startedAt: "2025-10-08T15:30:00Z",
    endedAt: "2025-10-08T17:15:00Z",
    date: "Oct 8, 2025",
    time: "3:30 PM",
    duration: "1h 45m",
    status: "completed",
    lastNoteAt: "2025-10-08T15:50:00Z",
    hasNotes: true,
  },
  {
    id: 303,
    caseId: 3,
    caseTitle: "Digital Transformation Initiative",
    mode: "Guided",
    startedAt: "2025-10-09T09:00:00Z",
    endedAt: "2025-10-09T10:30:00Z",
    date: "Oct 9, 2025",
    time: "9:00 AM",
    duration: "1h 30m", 
    status: "completed",
    lastNoteAt: "2025-10-09T10:15:00Z",
    hasNotes: true,
  },
]
const mockNotes = {
  101: [
    {
      id: "n1",
      sessionId: 101,
      caseId: 1,
      createdAt: "2025-10-05T10:20:00Z",
      updatedAt: "2025-10-05T10:20:00Z",
      content: "Hypothesis: supplier **B** is the bottleneck.",
    },
    {
      id: "n2", 
      sessionId: 101,
      caseId: 1,
      createdAt: "2025-10-05T10:35:00Z",
      updatedAt: "2025-10-05T10:35:00Z",
      content: "Chart on p.7 shows lead time spike.\n- Consider alt supplier\n- Negotiate buffer",
    },
  ],
  202: [
    {
      id: "n3",
      sessionId: 202,
      caseId: 2,
      createdAt: "2025-10-08T15:50:00Z",
      updatedAt: "2025-10-08T15:50:00Z",
      content: "Free chat: pricing sensitivity appears low; test bundles.",
    },
  ],
  303: [
    {
      id: "n4",
      sessionId: 303,
      caseId: 3,
      createdAt: "2025-10-09T10:15:00Z",
      updatedAt: "2025-10-09T10:15:00Z",
      content: "Key insight: Legacy systems integration is the main blocker for digital adoption.",
    },
  ],
}

// Utility function for consistent date formatting
function formatLastUpdated(dateString) {
  if (!dateString) return '—'
  
  return new Date(dateString).toLocaleDateString('en-US', { 
    month: 'short', 
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    hour12: true
  })
}

// Reusable mode badge component
function ModeBadge({ mode }) {
  return (
    <span className={
      "inline-flex items-center rounded-full px-2 py-0.5 text-xs border " +
      (mode === "Guided"
        ? "bg-blue-50 text-blue-700 border-blue-200"
        : "bg-slate-100 text-slate-700 border-slate-200")
    }>
      {mode}
    </span>
  )
}

// Reusable notes button component
function NotesButton({ session, onClick }) {
  if (!session.hasNotes) {
    return <span className="text-xs text-slate-400">—</span>
  }

  return (
    <button
      onClick={onClick}
      className="inline-flex items-center gap-1 text-sm text-blue-600 hover:text-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2 rounded px-2 py-1"
    >
      <FileText className="w-4 h-4" />
      Notes
    </button>
  )
}

// Reusable dropdown component
function SessionActionsDropdown({ sessionId, isOpen, onToggle, onAction }) {
  return (
    <div className="relative">
      <button 
        onClick={(e) => {
          e.stopPropagation()
          onToggle()
        }}
        className="p-1 hover:bg-slate-100 rounded focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2"
      >
        <MoreVertical className="w-4 h-4 text-slate-400" />
      </button>
      
      {isOpen && (
        <div className="absolute right-0 top-8 w-36 bg-white rounded-md shadow-lg border border-slate-200 py-1 z-10">
          <button
            onClick={() => onAction(sessionId, 'share')}
            className="w-full text-left px-3 py-2 text-sm text-slate-700 hover:bg-slate-50 flex items-center gap-2"
          >
            <Share className="w-4 h-4" />
            Share
          </button>
          <button
            onClick={() => onAction(sessionId, 'rename')}
            className="w-full text-left px-3 py-2 text-sm text-slate-700 hover:bg-slate-50 flex items-center gap-2"
          >
            <Edit className="w-4 h-4" />
            Rename
          </button>
          <button
            onClick={() => onAction(sessionId, 'archive')}
            className="w-full text-left px-3 py-2 text-sm text-slate-700 hover:bg-slate-50 flex items-center gap-2"
          >
            <Archive className="w-4 h-4" />
            Archive
          </button>
          <hr className="my-1 border-slate-200" />
          <button
            onClick={() => onAction(sessionId, 'delete')}
            className="w-full text-left px-3 py-2 text-sm text-red-600 hover:bg-red-50 flex items-center gap-2"
          >
            <Trash2 className="w-4 h-4" />
            Delete
          </button>
        </div>
      )}
    </div>
  )
}

export default function SessionHistory() {
  const [notesOpen, setNotesOpen] = useState(false)
  const [selectedSession, setSelectedSession] = useState(null)
  const [searchQuery, setSearchQuery] = useState("")
  const [activeDropdown, setActiveDropdown] = useState(null)
  const [isClosing, setIsClosing] = useState(false)
  const openerRef = useRef(null)

  const [params] = useSearchParams()
  const caseIdParam = params.get("caseId")

  const sessions = mockSessions.filter((s) => {
    const matchesSearch = s.caseTitle.toLowerCase().includes(searchQuery.toLowerCase())
    const matchesCase = !caseIdParam || String(s.caseId) === String(caseIdParam)
    return matchesSearch && matchesCase
  })

  const handleActionClick = (sessionId, action) => {
    setActiveDropdown(null)
    
    switch (action) {
      case 'share':
        console.log('Share session:', sessionId)
        // TODO: Implement share functionality
        break
      case 'rename':
        console.log('Rename session:', sessionId)
        // TODO: Implement rename functionality
        break
      case 'archive':
        console.log('Archive session:', sessionId)
        // TODO: Implement archive functionality
        break
      case 'delete':
        console.log('Delete session:', sessionId)
        // TODO: Implement delete functionality
        break
      default:
        break
    }
  }

  const handleClose = () => {
    setIsClosing(true)
    // Wait for animation to complete before actually closing
    setTimeout(() => {
      setNotesOpen(false)
      setIsClosing(false)
      setTimeout(() => openerRef.current?.focus?.(), 0)
    }, 300)
  }

  // Close dropdown when clicking outside
  useEffect(() => {
    const handleClickOutside = () => setActiveDropdown(null)
    if (activeDropdown) {
      document.addEventListener('click', handleClickOutside)
      return () => document.removeEventListener('click', handleClickOutside)
    }
  }, [activeDropdown])

  return (
    <div className="max-w-6xl mx-auto px-6 py-8">
      <h1 className="text-3xl font-bold text-slate-900">Session History</h1>
      <p className="text-slate-600 mt-1">Review your past case work and notes.</p>

      <div className="mt-6 rounded-2xl border border-slate-200 bg-white shadow-[0_2px_10px_rgba(2,6,23,.06)] p-4 md:p-6">
        {/* Search and filters section */}
        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-8">
          <h2 className="text-xl font-semibold text-slate-900">Your Sessions</h2>
          <div className="flex items-center gap-4">
            <div className="relative w-full sm:w-auto">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
              <input
                type="text"
                placeholder="Search sessions..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="w-full sm:w-64 pl-10 pr-4 py-2 h-10 border border-slate-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2"
              />
            </div>
          </div>
        </div>
        {/* Header row - Hidden on mobile */}
        <div className="hidden md:grid grid-cols-[140px_1fr_100px_120px_140px_100px_80px] gap-4 px-4 pb-3 border-b border-slate-200 text-sm font-medium text-slate-600">
          <div>Date/Time</div>
          <div>Case</div>
          <div>Mode</div>
          <div>Duration</div>
          <div>Last Updated</div>
          <div>Notes</div>
          <div>Actions</div>
        </div>

        {/* Rows */}
        <div className="divide-y divide-slate-100">
          {sessions.map((s) => (
            <div key={s.id}>
              {/* Desktop Layout */}
              <div className="hidden md:grid grid-cols-[140px_1fr_100px_120px_140px_100px_80px] gap-4 px-4 py-4 items-center hover:bg-slate-50/70 rounded-lg transition-colors">
                {/* Date/Time */}
                <div className="text-sm text-slate-700">
                  <div className="flex items-center gap-2">
                    <Calendar className="w-4 h-4 text-slate-400" />
                    {s.date}
                  </div>
                  <div className="text-xs text-slate-500 mt-0.5">{s.time}</div>
                </div>

                {/* Case */}
                <div className="text-sm font-medium text-slate-900">{s.caseTitle}</div>

                {/* Mode badge */}
                <div>
                  <ModeBadge mode={s.mode} />
                </div>

                {/* Duration */}
                <div className="flex items-center gap-2 text-sm text-slate-600">
                  <Clock className="w-4 h-4 text-slate-400" />
                  {s.duration}
                </div>

                {/* Last Updated */}
                <div className="text-sm text-slate-600">
                  {formatLastUpdated(s.lastNoteAt)}
                </div>

                {/* Notes */}
                <div>
                  <NotesButton 
                    session={s}
                    onClick={(e) => {
                      openerRef.current = e.currentTarget
                      setSelectedSession(s)
                      setNotesOpen(true)
                    }}
                  />
                </div>

                {/* Actions Menu */}
                <SessionActionsDropdown
                  sessionId={s.id}
                  isOpen={activeDropdown === s.id}
                  onToggle={() => setActiveDropdown(activeDropdown === s.id ? null : s.id)}
                  onAction={handleActionClick}
                />
              </div>

              {/* Mobile Layout */}
              <div className="md:hidden px-4 py-4 hover:bg-slate-50/70 rounded-lg transition-colors">
                <div className="flex justify-between items-start mb-3">
                  <div className="flex-1 min-w-0">
                    <h3 className="text-sm font-medium text-slate-900 truncate">{s.caseTitle}</h3>
                    <div className="flex items-center gap-2 text-xs text-slate-600 mt-1">
                      <Calendar className="w-3 h-3 text-slate-400" />
                      {s.date} • {s.time}
                      <span className="mx-1">•</span>
                      <Clock className="w-3 h-3 text-slate-400" />
                      {s.duration}
                    </div>
                  </div>
                  <div className="flex items-center gap-2 ml-3">
                    <ModeBadge mode={s.mode} />
                    <SessionActionsDropdown
                      sessionId={s.id}
                      isOpen={activeDropdown === s.id}
                      onToggle={() => setActiveDropdown(activeDropdown === s.id ? null : s.id)}
                      onAction={handleActionClick}
                    />
                  </div>
                </div>
                
                <div className="flex justify-between items-center">
                  <div className="text-xs text-slate-600">
                    Last updated: {formatLastUpdated(s.lastNoteAt) === '—' ? 'No updates' : formatLastUpdated(s.lastNoteAt)}
                  </div>
                  <NotesButton 
                    session={s}
                    onClick={(e) => {
                      openerRef.current = e.currentTarget
                      setSelectedSession(s)
                      setNotesOpen(true)
                    }}
                  />
                </div>
              </div>
            </div>
          ))}

          {sessions.length === 0 && (
            <div className="px-4 py-12 text-center text-slate-600">
              No sessions found.
            </div>
          )}
        </div>
      </div>

      {/* NotesDrawer stays mounted after the card so it overlays nicely */}
      <NotesDrawer
        open={notesOpen}
        onClose={handleClose}
        isClosing={isClosing}
        session={selectedSession}
      />
    </div>
  )
}

function NotesDrawer({ open, onClose, session, isClosing }) {
  const [mounted, setMounted] = useState(false)

  useEffect(() => {
    if (!open) return

    function onKey(e) {
      if (e.key === "Escape") onClose()
    }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [open, onClose])

  useEffect(() => {
    if (open) {
      // Small delay to ensure DOM is ready, then trigger the opening animation
      setTimeout(() => setMounted(true), 10)
    } else {
      setMounted(false)
    }
  }, [open])

  if (!open || !session) return null

  return (
    <>
      <style>{`
        .modal-overlay {
          opacity: 0;
          transition: opacity 0.3s ease-out;
        }
        .modal-overlay.mounted {
          opacity: 1;
        }
        .modal-content {
          transform: translateX(100%);
          transition: transform 0.3s ease-out;
        }
        .modal-content.mounted {
          transform: translateX(0);
        }
        .modal-overlay.closing {
          opacity: 0;
        }
        .modal-content.closing {
          transform: translateX(100%);
        }
      `}</style>
      {/* Backdrop */}
      <div
        className={`fixed inset-0 bg-black/50 z-50 modal-overlay ${mounted ? 'mounted' : ''} ${isClosing ? 'closing' : ''}`}
        onClick={onClose}
        aria-hidden="true"
      />

      {/* Sheet Content */}
      <div
        className={`fixed right-0 top-0 bottom-0 w-full sm:max-w-lg bg-white z-50 flex flex-col shadow-lg modal-content ${mounted ? 'mounted' : ''} ${isClosing ? 'closing' : ''}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby="notes-title"
        aria-describedby="notes-description"
      >
        {/* Sheet Header */}
        <div className="flex-shrink-0 px-6 py-6 border-b border-slate-200">
          <div className="flex items-start justify-between">
            <div className="min-w-0 flex-1">
              <h2 id="notes-title" className="text-lg font-semibold text-slate-900">
                Notes • {session.caseTitle}
              </h2>
              <p id="notes-description" className="mt-1 text-sm text-slate-600">
                {session.date} • {session.time} • {session.mode} mode • {session.duration}
              </p>
            </div>
            <button
              onClick={onClose}
              className="ml-3 flex-shrink-0 rounded-md text-slate-400 hover:text-slate-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              <span className="sr-only">Close</span>
              <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
        </div>

        {/* Sheet Body */}
        <div className="flex-1 overflow-y-auto px-6 py-6">
          <div className="space-y-6">
            {session.hasNotes ? (
              <div className="rounded-lg border border-slate-200 bg-slate-50/30 p-4">
                <div className="flex items-center gap-2 mb-3">
                  <span className="text-sm font-medium text-slate-900">📝 Session Notes</span>
                </div>
                <div className="prose prose-sm max-w-none">
                  <div className="text-sm text-slate-900 whitespace-pre-wrap leading-relaxed">
                    {/* Show combined notes content or individual notes */}
                    {(mockNotes[session.id] || []).map((n, index) => (
                      <div key={n.id} className={index > 0 ? "mt-4 pt-4 border-t border-slate-200" : ""}>
                        <div className="text-xs text-slate-500 mb-2">
                          {new Date(n.createdAt).toLocaleString()}
                        </div>
                        <div>{n.content}</div>
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            ) : (
              <div className="rounded-lg border border-dashed border-slate-200 bg-slate-50/20 p-8 text-center">
                <div className="w-8 h-8 text-slate-400 mx-auto mb-3">📝</div>
                <p className="text-sm text-slate-600">No notes recorded in this session.</p>
              </div>
            )}
            
            {/* Session Details Footer */}
            <div className="flex items-center gap-2 text-xs text-slate-500 pt-4 border-t border-slate-200">
              <Clock className="w-3 h-3" />
              <span>Session duration: {session.duration}</span>
              {session.lastNoteAt && (
                <>
                  <span className="mx-2">•</span>
                  <span>Last updated: {formatLastUpdated(session.lastNoteAt)}</span>
                </>
              )}
            </div>
          </div>
        </div>
      </div>
    </>
  )
}
