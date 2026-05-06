// src/components/WorkspaceNotesPanel.clean.jsx
import { useState, useEffect, useRef } from 'react'
import { Card } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { StickyNote, X, Check } from 'lucide-react'
import toast from 'react-hot-toast'
import { listSessionNotes, addSessionNote, updateSessionNote, deleteSessionNote } from '@/lib/api'

/**
 * Props:
 * - open (bool)
 * - onOpenChange (fn)
 * - currentCaseId (string)
 * - currentSessionId (string)
 * - panelRef (ref?) optional
 */
export default function WorkspaceNotesPanel({
  open,
  onOpenChange,
  currentCaseId,
  currentSessionId,
  panelRef = null,
}) {
  const localPanelRef = useRef(null)

  // Do we have a real persisted session, or just the "session-current" placeholder?
  const hasSession = currentSessionId && currentSessionId !== 'session-current'

  // ---- Notes from backend (per session) ----
  const [notes, setNotes] = useState([])
  const [notesLoading, setNotesLoading] = useState(false)
  const [notesError, setNotesError] = useState(null)
  const [editingId, setEditingId] = useState(null)
  const [editText, setEditText] = useState('')

  // Load notes from backend when the panel opens and we have a real session
  useEffect(() => {
    if (!open || !hasSession) {
      setNotes([])
      setNotesError(null)
      setNotesLoading(false)
      return
    }

    let cancelled = false
    async function loadNotes() {
      setNotesLoading(true)
      setNotesError(null)
      try {
        const data = await listSessionNotes(currentSessionId)
        if (!cancelled) {
          setNotes(Array.isArray(data) ? data : [])
        }
      } catch (err) {
        console.error('[Notes] fetch failed', err)
        if (!cancelled) {
          setNotesError('Failed to load notes')
          setNotes([])
          toast.error('Failed to load notes')
        }
      } finally {
        if (!cancelled) {
          setNotesLoading(false)
        }
      }
    }

    loadNotes()

    return () => {
      cancelled = true
    }
  }, [open, currentSessionId, hasSession])

  // ---- Local autosave state for the composer ----
  const storageKey = `notes:${currentCaseId || 'no-id'}:${currentSessionId || 'default'}`
  const [text, setText] = useState('')
  const debounceRef = useRef(null)

  // Load saved draft text when panel opens
  useEffect(() => {
    if (!open) return
    try {
      const saved = localStorage.getItem(storageKey)
      if (saved != null) setText(saved)
    } catch {
      // ignore localStorage errors in dev
    }
  }, [open, storageKey])

  // Debounced autosave of the draft text
  useEffect(() => {
    if (!open) return
    if (debounceRef.current) clearTimeout(debounceRef.current)
    if (text == null) return

    debounceRef.current = setTimeout(() => {
      try {
        localStorage.setItem(storageKey, text)
      } catch {
        console.warn('[Notes] local draft save failed')
      }
    }, 600)

    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current)
    }
  }, [text, open, storageKey])

  // ---- Drawer mount/close animations ----
  const [mounted, setMounted] = useState(false)
  const [isClosing, setIsClosing] = useState(false)

  useEffect(() => {
    if (open) {
      // Small delay so DOM is ready then trigger mounted state
      setTimeout(() => setMounted(true), 10)
    } else {
      setMounted(false)
    }
  }, [open])

  function requestClose() {
    setIsClosing(true)
    // wait for animation to finish, then notify parent and reset
    setTimeout(() => {
      setIsClosing(false)
      setMounted(false)
      onOpenChange(false)
    }, 300)
  }

  // Focus management on open
  useEffect(() => {
    if (!mounted) return
    const containerRef = panelRef && panelRef.current ? panelRef.current : localPanelRef.current
    if (!containerRef) return
    const prevActive = document.activeElement
    const focusable = containerRef.querySelector(
      'button, [href], input, textarea, select, [tabindex]:not([tabindex="-1"])'
    )
    try {
      ;(focusable || containerRef).focus()
    } catch {
      /* ignore */
    }
    return () => {
      try {
        prevActive?.focus?.()
      } catch {
        /* ignore */
      }
    }
  }, [mounted, panelRef])

  if (!open) return null

  // Save current text as a backend note (does not clear the draft until success)
  async function addNote() {
    const content = text.trim()
    if (!content) return

    if (!hasSession) {
      toast.error('Start a Q&A session by asking a question before saving notes.')
      return
    }

    try {
      const created = await addSessionNote(currentSessionId, content)
      // The API returns { id, text, createdAt }
      setNotes(prev => [
        {
          id: created.id,
          text: created.text,
          createdAt: created.createdAt,
        },
        ...(prev || []),
      ])
      // clear composer + draft
      setText('')
      try {
        localStorage.removeItem(storageKey)
      } catch {
        /* ignore */
      }
      toast.success('Note saved')
    } catch (err) {
      console.error('[Notes] add failed', err)
      toast.error('Failed to save note')
    }
  }

  async function saveEdit(noteId) {
    const newText = (editText || '').trim()
    if (!newText) {
      toast.error('Note cannot be empty')
      return
    }
    if (!hasSession) {
      toast.error('Start a Q&A session before editing notes.')
      return
    }
    try {
      await updateSessionNote(currentSessionId, noteId, newText)
      setNotes(prev => prev.map(n => (n.id === noteId ? { ...n, text: newText } : n)))
      setEditingId(null)
      setEditText('')
      toast.success('Note updated')
    } catch (err) {
      console.error('[Notes] update failed', err)
      toast.error('Failed to update note')
    }
  }

  // Named handler to avoid inline arrow in JSX
  function handleTextChange(e) {
    setText(e.target.value)
  }

  return (
    <>
      <style>{`
        .ws-overlay { opacity: 0; transition: opacity 0.28s ease-out; }
        .ws-overlay.mounted { opacity: 1; }
        .ws-overlay.closing { opacity: 0; }
        .ws-content { transform: translateX(100%); transition: transform 0.28s ease-out; }
        .ws-content.mounted { transform: translateX(0); }
        .ws-content.closing { transform: translateX(100%); }
      `}</style>

      {/* Backdrop (visual only, click-through) */}
      <div
        className={`fixed inset-0 bg-black/10 z-40 ws-overlay pointer-events-none ${
          mounted ? 'mounted' : ''
        } ${isClosing ? 'closing' : ''}`}
        aria-hidden="true"
      />

      {/* Panel */}
      <div
        ref={panelRef || localPanelRef}
        tabIndex={-1}
        className={`fixed right-0 top-0 bottom-0 w-full sm:max-w-lg bg-[#faf8f5] border-l border-[#e4d6c7] z-60 shadow-xl flex flex-col ws-content ${
          mounted ? 'mounted' : ''
        } ${isClosing ? 'closing' : ''}`}
        role="region"
        aria-modal="false"
        aria-label="Notes panel"
      >
        <div className="p-4 border-b border-[#e4d6c7] flex items-center justify-between bg-[#faf8f5]">
          <div>
            <h3 className="font-semibold text-[#5C4C3C] flex items-center gap-2">
              <StickyNote className="w-4 h-4" /> Notes
            </h3>
          </div>
          <Button
            variant="ghost"
            size="sm"
            onClick={requestClose}
            aria-label="Close notes"
            className="text-[#5C4C3C] hover:text-[#C96A0A] hover:bg-[#F6EEE5] rounded-full"
          >
            <X className="w-4 h-4" />
          </Button>
        </div>

        {/* Composer (autosaved draft) */}
        <div className="p-4 border-b border-[#e4d6c7] bg-[#faf8f5]">
          <Textarea
            value={text}
            onChange={handleTextChange}
            placeholder="Write a quick note..."
            className="min-h-[160px] p-3 resize-y bg-white text-[#5C4C3C] border border-[#e4d6c7] placeholder-[#9a8577]"
          />
          <div className="mt-3 flex justify-end gap-2">
            <Button 
              variant="outline" 
              size="default"
              onClick={() => setText('')}
              className="text-[#5C4C3C] border-[#d4c4b0] bg-white hover:bg-[#faf8f5]"
            >
              Clear
            </Button>
            <Button 
              variant="warm" 
              size="default"
              onClick={addNote}
              className="gap-2"
            >
              <Check className="w-4 h-4" />
              Save note
            </Button>
          </div>
        </div>

        {/* Notes list */}
        <div className="p-4 space-y-3 overflow-auto flex-1 bg-[#faf8f5]">
          {notesLoading ? (
            <Card className="p-6 text-sm text-[#9a8577] bg-white border-[#e4d6c7]">Loading notes…</Card>
          ) : notesError ? (
            <Card className="p-6 text-sm text-red-600 bg-white border-[#e4d6c7]">{notesError}</Card>
          ) : notes.length === 0 ? (
            <Card className="p-6 text-sm text-[#9a8577] bg-white border-[#e4d6c7]">No notes yet.</Card>
          ) : (
            notes.map(n => (
              <Card key={n.id} className="p-4 bg-white border-[#e4d6c7]">
                <div className="flex items-start justify-between gap-2">
                  <div className="text-xs text-muted-foreground mb-2">{n.createdAt}</div>
                  <div className="flex items-center gap-2">
                    {editingId === n.id ? (
                      <>
                        <Button size="sm" variant="secondary" onClick={() => saveEdit(n.id)}>
                          Save
                        </Button>
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => {
                            setEditingId(null)
                            setEditText('')
                          }}
                        >
                          Cancel
                        </Button>
                      </>
                    ) : (
                      <>
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => {
                            setEditingId(n.id)
                            setEditText(n.text || '')
                          }}
                        >
                          Edit
                        </Button>
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={async () => {
                            if (!window.confirm('Delete this note?')) return
                            try {
                              await deleteSessionNote(currentSessionId, n.id)
                              setNotes(prev => prev.filter(x => x.id !== n.id))
                              toast.success('Note deleted')
                            } catch (err) {
                              console.error('[Notes] delete failed', err)
                              toast.error('Failed to delete note')
                            }
                          }}
                        >
                          Delete
                        </Button>
                      </>
                    )}
                  </div>
                </div>
                {editingId === n.id ? (
                  <Textarea
                    value={editText}
                    onChange={e => setEditText(e.target.value)}
                    className="mt-2 min-h-[100px]"
                  />
                ) : (
                  <pre className="whitespace-pre-wrap text-sm text-foreground">{n.text}</pre>
                )}
              </Card>
            ))
          )}
        </div>
      </div>
    </>
  )
}
