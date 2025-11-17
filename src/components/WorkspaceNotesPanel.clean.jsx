// src/components/WorkspaceNotesPanel.clean.jsx
import { useState, useEffect, useRef } from 'react'
import { Card } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { StickyNote, X } from 'lucide-react'
import toast from 'react-hot-toast'
import { listSessionNotes, addSessionNote } from '@/lib/api'

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
  const [saveStatus, setSaveStatus] = useState('idle') // 'idle' | 'saving' | 'saved' | 'error'
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
    setSaveStatus('idle')
  }, [open, storageKey])

  // Debounced autosave of the draft text
  useEffect(() => {
    if (!open) return
    if (debounceRef.current) clearTimeout(debounceRef.current)
    if (text == null) return

    setSaveStatus('saving')
    debounceRef.current = setTimeout(() => {
      try {
        localStorage.setItem(storageKey, text)
        setSaveStatus('saved')
      } catch {
        setSaveStatus('error')
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
      setSaveStatus('saving')
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
      setSaveStatus('saved')
      try {
        localStorage.removeItem(storageKey)
      } catch {
        /* ignore */
      }
      toast.success('Note saved')
    } catch (err) {
      console.error('[Notes] add failed', err)
      setSaveStatus('error')
      toast.error('Failed to save note')
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
        className={`fixed right-0 top-0 bottom-0 w-full sm:max-w-lg bg-white border-l border-border z-60 shadow-xl flex flex-col ws-content ${
          mounted ? 'mounted' : ''
        } ${isClosing ? 'closing' : ''}`}
        role="region"
        aria-modal="false"
        aria-label="Notes panel"
      >
        <div className="p-4 border-b border-border flex items-center justify-between">
          <div>
            <h3 className="font-semibold text-foreground flex items-center gap-2">
              <StickyNote className="w-4 h-4" /> Notes
            </h3>
            <p className="text-xs text-muted-foreground mt-1">
              {saveStatus === 'saving'
                ? 'Saving…'
                : saveStatus === 'saved'
                  ? 'Saved'
                  : saveStatus === 'error'
                    ? 'Save failed'
                    : ''}
            </p>
            {!hasSession && (
              <p className="text-[11px] text-muted-foreground mt-1">
                Start a Q&amp;A session first – notes are attached to sessions.
              </p>
            )}
          </div>
          <Button variant="ghost" size="sm" onClick={requestClose} aria-label="Close notes">
            <X className="w-4 h-4" />
          </Button>
        </div>

        {/* Composer (autosaved draft) */}
        <div className="p-4 border-b border-border bg-white">
          <Textarea
            value={text}
            onChange={handleTextChange}
            placeholder="Write a quick note..."
            className="min-h-[160px] p-3 resize-y bg-white text-foreground border border-slate-200"
          />
          <div className="mt-2 flex justify-end">
            <Button onClick={addNote} disabled={!hasSession}>
              Save note
            </Button>
          </div>
        </div>

        {/* Notes list */}
        <div className="p-4 space-y-3 overflow-auto flex-1 bg-white">
          {notesLoading ? (
            <Card className="p-6 text-sm text-muted-foreground bg-white">Loading notes…</Card>
          ) : notesError ? (
            <Card className="p-6 text-sm text-red-600 bg-white">{notesError}</Card>
          ) : notes.length === 0 ? (
            <Card className="p-6 text-sm text-muted-foreground bg-white">No notes yet.</Card>
          ) : (
            notes.map(n => (
              <Card key={n.id} className="p-4 bg-white">
                <div className="text-xs text-muted-foreground mb-2">{n.createdAt}</div>
                <pre className="whitespace-pre-wrap text-sm text-foreground">{n.text}</pre>
              </Card>
            ))
          )}
        </div>
      </div>
    </>
  )
}
