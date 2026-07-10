import { useState, useRef, useEffect } from 'react'
import { useSearchParams, useNavigate } from 'react-router-dom'
import {
  Calendar,
  Clock,
  MoreVertical,
  Search,
  Share,
  Edit,
  Archive,
  Trash2,
  FileText,
} from 'lucide-react'
import { getPagedItems, listSessionsMine, listSessionNotes, renameCase } from '@/lib/api'
import { deleteSession } from '@/lib/api'
import toast from 'react-hot-toast'
import { API_BASE } from '@/config'
// Utility function for consistent date formatting
function formatLastUpdated(dateString) {
  if (!dateString) return '—'

  return new Date(dateString).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    hour12: true,
  })
}

function formatDurationFromSeconds(sec) {
  if (!sec || sec <= 0) return '—'
  const totalMinutes = Math.round(sec / 60)
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  if (hours && minutes) return `${hours}h ${minutes}m`
  if (hours) return `${hours}h`
  return `${minutes} min`
}

function mapApiSessionToView(api) {
  const started = new Date(api.createdAt)
  const date = started.toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  })
  const time = started.toLocaleTimeString('en-US', {
    hour: 'numeric',
    minute: '2-digit',
    hour12: true,
  })

  return {
    id: api.sessionId,
    // keep the raw uploadId so we can tell “case” vs “free chat”
    uploadId: api.uploadId || null,
    caseId: api.uploadId,
    isFreeChat: !api.uploadId, // used only for filtering
    // this comes from Program.cs COALESCE(u.Name, u.OriginalFileName, 'Untitled case')
    caseTitle: api.caseName || 'Untitled case',
    startedAt: api.createdAt,
    endedAt: api.lastActivityAt,
    date,
    time,
    duration: formatDurationFromSeconds(api.durationSec),
    status: 'completed',
    lastNoteAt: api.lastActivityAt,
    hasNotes: (api.notesCount ?? 0) > 0,
  }
}

function NotesButton({ session, onClick }) {
  if (!session.hasNotes) {
    return <span className="text-xs text-slate-400">No notes</span>
  }

  return (
    <button
      onClick={e => {
        // Prevent the row's onClick from firing
        e.stopPropagation()
        if (onClick) {
          onClick(e)
        }
      }}
      className="inline-flex items-center gap-1 text-sm text-blue-600 hover:text-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2 rounded px-2 py-1"
    >
      <FileText className="w-4 h-4" />
      Notes
    </button>
  )
}

function SessionActionsDropdown({ sessionId, isOpen, onToggle, onAction }) {
  return (
    <div className="relative">
      <button
        onClick={e => {
          e.stopPropagation()
          onToggle()
        }}
        className="p-1 hover:bg-[#f3e8dc] rounded-md focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2 transition-colors"
      >
        <MoreVertical className="w-4 h-4 text-slate-400" />
      </button>

      {isOpen && (
        <div className="absolute right-0 top-8 w-36 bg-white rounded-md shadow-lg border border-slate-200 py-1 z-10">
          <button
            onClick={e => {
              e.stopPropagation()
              onAction(sessionId, 'share')
            }}
            className="w-full text-left px-3 py-2 text-sm text-slate-700 hover:bg-slate-50 flex items-center gap-2"
          >
            <Share className="w-4 h-4" />
            Share
          </button>
          <button
            onClick={e => {
              e.stopPropagation()
              onAction(sessionId, 'rename')
            }}
            className="w-full text-left px-3 py-2 text-sm text-slate-700 hover:bg-slate-50 flex items-center gap-2"
          >
            <Edit className="w-4 h-4" />
            Rename
          </button>
          <button
            onClick={e => {
              e.stopPropagation()
              onAction(sessionId, 'archive')
            }}
            className="w-full text-left px-3 py-2 text-sm text-slate-700 hover:bg-slate-50 flex items-center gap-2"
          >
            <Archive className="w-4 h-4" />
            Archive
          </button>
          <hr className="my-1 border-slate-200" />
          <button
            onClick={e => {
              e.stopPropagation()
              onAction(sessionId, 'delete')
            }}
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
  const [sessions, setSessions] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  const [notesOpen, setNotesOpen] = useState(false)
  const [selectedSession, setSelectedSession] = useState(null)
  const [searchQuery, setSearchQuery] = useState('')
  const [activeDropdown, setActiveDropdown] = useState(null)
  const [isClosing, setIsClosing] = useState(false)
  const openerRef = useRef(null)

  const [notesBySession, setNotesBySession] = useState({})
  const [loadingNotesFor, setLoadingNotesFor] = useState(null)

  const [editingSessionId, setEditingSessionId] = useState(null)
  const [editingTitle, setEditingTitle] = useState('')

  const [deleteTarget, setDeleteTarget] = useState(null)
  const [deleting, setDeleting] = useState(false)

  const [params] = useSearchParams()
  const caseIdParam = params.get('caseId')

  const navigate = useNavigate()

  // Load sessions from backend
  useEffect(() => {
    let cancelled = false

    async function loadSessions() {
      try {
        setLoading(true)
        setError(null)
        const apiSessions = await listSessionsMine()
        if (cancelled) return
        const mapped = getPagedItems(apiSessions).map(mapApiSessionToView)
        setSessions(mapped)
      } catch (err) {
        console.error('Failed to load sessions', err)
        if (!cancelled) {
          setError(err.message || 'Failed to load sessions')
        }
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    loadSessions()
    return () => {
      cancelled = true
    }
  }, [])

  const filteredSessions = sessions.filter(s => {
    const matchesSearch = s.caseTitle.toLowerCase().includes(searchQuery.toLowerCase())
    const matchesCase = !caseIdParam || String(s.caseId) === String(caseIdParam)
    return matchesSearch && matchesCase
  })

  async function loadNotes(sessionId) {
    try {
      setLoadingNotesFor(sessionId)
      const apiNotes = await listSessionNotes(sessionId)
      const mappedNotes = getPagedItems(apiNotes).map(n => ({
        id: n.id,
        createdAt: n.createdAt,
        content: n.text,
      }))
      setNotesBySession(prev => ({
        ...prev,
        [sessionId]: mappedNotes,
      }))
    } catch (err) {
      console.error('Failed to load notes', err)
    } finally {
      setLoadingNotesFor(prev => (prev === sessionId ? null : prev))
    }
  }

  const handleActionClick = async (sessionId, action) => {
    setActiveDropdown(null)

    const session = sessions.find(s => s.id === sessionId)
    if (!session) return

    switch (action) {
      case 'share': {
        // Link to the workspace for this session/case
        const appBase = import.meta.env.BASE_URL || '/'
        const targetId = session.caseId || session.id
        const url = `${window.location.origin}${appBase}workspace/${encodeURIComponent(targetId)}`
        try {
          await navigator.clipboard.writeText(url)
          toast.success('Link copied')
        } catch (err) {
          console.warn(err)
          toast.error('Copy failed')
        }
        break
      }

      case 'rename': {
        if (!session.caseId || session.caseId === session.id) {
          toast.error('Renaming is only supported for case-based sessions right now.')
          return
        }
        setEditingSessionId(sessionId)
        setEditingTitle(getSessionTitle(session))
        break
      }

      case 'archive': {
        // Backend doesn’t support archive yet — just stub for now
        toast('Archive is not implemented yet.')
        break
      }

      case 'delete': {
        setDeleteTarget(session)
        break
      }

      default:
        break
    }
  }

  const handleRenameSave = async sessionId => {
    const newTitle = editingTitle.trim()
    if (!newTitle) {
      toast.error('Title cannot be empty')
      return
    }

    const session = sessions.find(s => s.id === sessionId)
    if (!session) return

    if (!session.caseId || session.caseId === session.id) {
      toast.error('Renaming is only supported for case-based sessions.')
      return
    }

    try {
      await renameCase(session.caseId, newTitle)

      setSessions(prev =>
        prev.map(s =>
          s.id === sessionId
            ? {
                ...s,
                caseTitle: newTitle,
              }
            : s
        )
      )

      toast.success('Case renamed')
      setEditingSessionId(null)
      setEditingTitle('')
    } catch (err) {
      console.warn(err)
      toast.error(err?.message || 'Rename failed')
    }
  }

  const handleRenameCancel = () => {
    setEditingSessionId(null)
    setEditingTitle('')
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
  function getSessionTitle(s) {
    // Prefer the mapped caseTitle from mapApiSessionToView
    if (s.caseTitle && s.caseTitle.trim()) {
      return s.caseTitle.replace(/\.pdf$/i, '') // strip .pdf if present
    }

    // If it’s tied to an upload but somehow has no name, call it Untitled case
    if (s.uploadId) {
      return 'Untitled case'
    }

    // Sessions with no uploadId are pure “free chat”
    return 'Free chat'
  }

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
                onChange={e => setSearchQuery(e.target.value)}
                className="w-full sm:w-64 pl-10 pr-4 py-2 h-10 border border-slate-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2"
              />
            </div>
          </div>
        </div>

        {loading && (
          <div className="px-4 py-12 text-center text-slate-600">Loading sessions...</div>
        )}

        {error && !loading && (
          <div className="px-4 py-12 text-center text-red-600 text-sm">{error}</div>
        )}

        {!loading && !error && (
          <>
            {/* Header row - Hidden on mobile */}
            <div className="hidden md:grid grid-cols-[140px_1fr_120px_140px_100px_80px] gap-4 px-4 pb-3 border-b border-slate-200 text-sm font-medium text-slate-600">
              <div>Date/Time</div>
              <div>Case</div>
              <div>Duration</div>
              <div>Last Updated</div>
              <div>Notes</div>
              <div>Actions</div>
            </div>

            {/* Rows */}
            <div className="divide-y divide-slate-100">
              {filteredSessions.map(s => (
                <div key={s.id}>
                  {/* Desktop Layout */}
                  <div
                    className="hidden md:grid grid-cols-[140px_1fr_120px_140px_100px_80px] gap-4 px-4 py-4 items-center hover:bg-[#f9f6f1] rounded-lg transition-colors cursor-pointer"
                    onClick={() => {
                      // uploadId is the case (PDF). Fallback to sessionId for free chat.
                      const uploadId = s.uploadId || s.caseId || s.id

                      // Always pass the sessionId so Workspace can restore the conversation
                      const url = `/workspace/${encodeURIComponent(uploadId)}?sessionId=${encodeURIComponent(
                        s.id
                      )}`

                      navigate(url)
                    }}
                  >
                    {/* Date/Time */}
                    <div className="text-sm text-slate-700">
                      <div className="flex items-center gap-2">
                        <Calendar className="w-4 h-4 text-slate-400" />
                        {s.date}
                      </div>
                      <div className="text-xs text-slate-500 mt-0.5">{s.time}</div>
                    </div>

                    {/* Case */}
                    <div className="min-w-0">
                      {editingSessionId === s.id ? (
                        <div className="flex items-center gap-2">
                          <input
                            type="text"
                            value={editingTitle}
                            onChange={e => setEditingTitle(e.target.value)}
                            className="w-full px-2 py-1 border border-slate-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-300/60"
                            autoFocus
                          />
                          <button
                            onClick={e => {
                              e.stopPropagation()
                              handleRenameSave(s.id)
                            }}
                            className="px-2 py-1 text-xs rounded bg-blue-600 text-white hover:bg-blue-700"
                          >
                            Save
                          </button>
                          <button
                            onClick={e => {
                              e.stopPropagation()
                              handleRenameCancel()
                            }}
                            className="px-2 py-1 text-xs rounded border border-slate-300 text-slate-700 hover:bg-slate-50"
                          >
                            Cancel
                          </button>
                        </div>
                      ) : (
                        <div
                          className="text-sm font-semibold text-slate-900 truncate"
                          title={getSessionTitle(s)}
                        >
                          {getSessionTitle(s)}
                        </div>
                      )}
                    </div>

                    {/* Mode column removed */}

                    {/* Duration */}
                    <div className="flex items-center gap-2 text-sm text-slate-600">
                      <Clock className="w-4 h-4 text-slate-400" />
                      {s.duration}
                    </div>

                    {/* Last Updated */}
                    <div className="text-sm text-slate-600">{formatLastUpdated(s.lastNoteAt)}</div>

                    {/* Notes */}
                    <div>
                      <NotesButton
                        session={s}
                        onClick={e => {
                          openerRef.current = e.currentTarget
                          setSelectedSession(s)
                          setNotesOpen(true)
                          if (!notesBySession[s.id]) {
                            loadNotes(s.id)
                          }
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
                  <div
                    className="md:hidden px-4 py-4 hover:bg-[#f9f6f1] rounded-lg transition-colors cursor-pointer"
                    onClick={() => {
                      const uploadId = s.uploadId || s.caseId || s.id

                      const url = `/workspace/${encodeURIComponent(uploadId)}?sessionId=${encodeURIComponent(
                        s.id
                      )}`

                      navigate(url)
                    }}
                  >
                    <div className="flex justify-between items-start mb-3">
                      <div className="flex-1 min-w-0">
                        <h3 className="text-sm font-semibold text-slate-900 truncate">
                          {editingSessionId === s.id ? (
                            <input
                              type="text"
                              value={editingTitle}
                              onChange={e => setEditingTitle(e.target.value)}
                              className="w-full px-2 py-1 border border-slate-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-300/60"
                              autoFocus
                              onClick={e => e.stopPropagation()}
                            />
                          ) : (
                            getSessionTitle(s)
                          )}
                        </h3>

                        <div className="flex items-center gap-2 text-xs text-slate-600 mt-1">
                          <Calendar className="w-3 h-3 text-slate-400" />
                          {s.date} • {s.time}
                          <span className="mx-1">•</span>
                          <Clock className="w-3 h-3 text-slate-400" />
                          {s.duration}
                        </div>
                      </div>
                      <div className="flex items-center gap-2 ml-3">
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
                        Last updated:{' '}
                        {formatLastUpdated(s.lastNoteAt) === '—'
                          ? 'No updates'
                          : formatLastUpdated(s.lastNoteAt)}
                      </div>
                      <NotesButton
                        session={s}
                        onClick={e => {
                          openerRef.current = e.currentTarget
                          setSelectedSession(s)
                          setNotesOpen(true)
                          if (!notesBySession[s.id]) {
                            loadNotes(s.id)
                          }
                        }}
                      />
                    </div>
                  </div>
                </div>
              ))}

              {filteredSessions.length === 0 && (
                <div className="px-4 py-12 text-center text-slate-600">No sessions found.</div>
              )}
            </div>
          </>
        )}
      </div>

      {/* Delete confirm dialog */}
      {deleteTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-lg shadow-lg p-6 max-w-sm w-full">
            <h2 className="text-sm font-semibold text-slate-900 mb-2">Delete this session?</h2>
            <p className="text-sm text-slate-600 mb-4">
              This will permanently delete the session and its messages/notes. This action cannot be
              undone.
            </p>
            <div className="flex justify-end gap-2">
              <button
                onClick={() => setDeleteTarget(null)}
                className="px-3 py-1.5 text-sm rounded border border-slate-300 text-slate-700 hover:bg-slate-50"
                disabled={deleting}
              >
                Cancel
              </button>
              <button
                onClick={async () => {
                  if (!deleteTarget) return
                  setDeleting(true)
                  try {
                    await deleteSession(deleteTarget.id)
                    setSessions(prev => prev.filter(s => s.id !== deleteTarget.id))
                    toast.success('Session deleted')
                    setDeleteTarget(null)
                  } catch (err) {
                    console.warn(err)
                    toast.error(err?.message || 'Delete failed')
                  } finally {
                    setDeleting(false)
                  }
                }}
                className="px-3 py-1.5 text-sm rounded bg-red-600 text-white hover:bg-red-700 disabled:opacity-60"
                disabled={deleting}
              >
                {deleting ? 'Deleting…' : 'Delete'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* NotesDrawer */}
      <NotesDrawer
        open={notesOpen}
        onClose={handleClose}
        isClosing={isClosing}
        session={selectedSession}
        notes={selectedSession ? notesBySession[selectedSession.id] || [] : []}
        loadingNotes={selectedSession ? loadingNotesFor === selectedSession.id : false}
      />
    </div>
  )
}

function NotesDrawer({ open, onClose, session, isClosing, notes, loadingNotes }) {
  const [mounted, setMounted] = useState(false)

  useEffect(() => {
    if (!open) return

    function onKey(e) {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
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
        className={`fixed inset-0 bg-black/50 z-50 modal-overlay ${
          mounted ? 'mounted' : ''
        } ${isClosing ? 'closing' : ''}`}
        onClick={onClose}
        aria-hidden="true"
      />

      {/* Sheet Content */}
      <div
        className={`fixed right-0 top-0 bottom-0 w-full sm:max-w-lg bg-white z-50 flex flex-col shadow-lg modal-content ${
          mounted ? 'mounted' : ''
        } ${isClosing ? 'closing' : ''}`}
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
                <span className="block truncate max-w-full">Notes • {session.caseTitle}</span>
              </h2>
              <p id="notes-description" className="mt-1 text-sm text-slate-600">
                {session.date} • {session.time} • {session.duration}
              </p>
            </div>
            <button
              onClick={onClose}
              className="ml-3 flex-shrink-0 rounded-md text-slate-400 hover:text-slate-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              <span className="sr-only">Close</span>
              <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M6 18L18 6M6 6l12 12"
                />
              </svg>
            </button>
          </div>
        </div>

        {/* Sheet Body */}
        <div className="flex-1 overflow-y-auto px-6 py-6">
          <div className="space-y-6">
            {session.hasNotes ? (
              loadingNotes ? (
                <div className="rounded-lg border border-slate-200 bg-slate-50/30 p-8 text-center text-sm text-slate-600">
                  Loading notes...
                </div>
              ) : notes && notes.length > 0 ? (
                <div className="rounded-lg border border-slate-200 bg-slate-50/30 p-4">
                  <div className="flex items-center gap-2 mb-3">
                    <span className="text-sm font-medium text-slate-900">📝 Session Notes</span>
                  </div>
                  <div className="prose prose-sm max-w-none">
                    <div className="text-sm text-slate-900 whitespace-pre-wrap leading-relaxed">
                      {notes.map((n, index) => (
                        <div
                          key={n.id}
                          className={index > 0 ? 'mt-4 pt-4 border-t border-slate-200' : ''}
                        >
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
              )
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
