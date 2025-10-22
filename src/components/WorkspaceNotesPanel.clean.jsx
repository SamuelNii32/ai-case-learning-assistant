import { useState, useEffect, useRef } from 'react'
import { Card } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { StickyNote, X } from 'lucide-react'

/**
 * Props:
 * - open (bool)
 * - onOpenChange (fn)
 * - currentCaseId (string)
 * - currentSessionId (string)
 */
export default function WorkspaceNotesPanel({
  open,
  onOpenChange,
  currentCaseId,
  currentSessionId,
  panelRef = null,
}) {
  const localPanelRef = useRef(null)
  const [notes, setNotes] = useState([
    {
      id: 'n1',
      createdAt: '2h ago',
      text: 'Key findings: triage bottlenecks and lack of bed tracking.\nNext: quantify average delays per stage.',
    },
  ])
  const [draft, setDraft] = useState('')

  // Use the same mount/closing pattern as SessionHistory's NotesDrawer so animations are consistent.
  const [mounted, setMounted] = useState(false)
  const [isClosing, setIsClosing] = useState(false)

  useEffect(() => {
    if (open) {
      // Small delay so DOM is ready then trigger mounted state which will play CSS animation
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

  // Render as long as `open` is true. `mounted` controls the mounted CSS class
  // so the enter animation can run when the component first appears.
  // Focus management: set initial focus inside the panel when it mounts
  useEffect(() => {
    if (!mounted) return
    const ref = panelRef && panelRef.current ? panelRef.current : localPanelRef.current
    if (!ref) return
    const prevActive = document.activeElement
    // pick the first focusable element inside the panel (button, textarea, input, etc.)
    const focusable = ref.querySelector(
      'button, [href], input, textarea, select, [tabindex]:not([tabindex="-1"])'
    )
    try {
      ;(focusable || ref).focus()
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

  function addNote() {
    const text = draft.trim()
    if (!text) return
    setNotes(prev => [{ id: String(Date.now()), createdAt: 'just now', text }, ...prev])
    setDraft('')
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
        className={`fixed inset-0 bg-black/10 z-40 ws-overlay pointer-events-none ${mounted ? 'mounted' : ''} ${isClosing ? 'closing' : ''}`}
        aria-hidden="true"
      />

      {/* Panel */}
      <div
        ref={panelRef}
        tabIndex={-1}
        className={`fixed right-0 top-0 bottom-0 w-full sm:max-w-lg bg-white border-l border-border z-60 shadow-xl flex flex-col ws-content ${mounted ? 'mounted' : ''} ${isClosing ? 'closing' : ''}`}
        role="region"
        aria-modal="false"
        aria-label="Notes panel"
      >
        <div className="p-4 border-b border-border flex items-center justify-between">
          <div>
            <h3 className="font-semibold text-foreground flex items-center gap-2">
              <StickyNote className="w-4 h-4" /> Notes
            </h3>
            <p className="text-xs text-muted-foreground">
              Case: {currentCaseId || '—'} • Session: {currentSessionId || '—'}
            </p>
          </div>
          <Button variant="ghost" size="sm" onClick={requestClose} aria-label="Close notes">
            <X className="w-4 h-4" />
          </Button>
        </div>

        {/* Composer */}
        <div className="p-4 border-b border-border bg-white">
          <Textarea
            value={draft}
            onChange={e => setDraft(e.target.value)}
            placeholder="Write a quick note…"
            className="min-h-[160px] p-3 resize-y bg-white text-foreground border border-slate-200"
          />
          <div className="mt-2 flex justify-end">
            <Button onClick={addNote}>Save note</Button>
          </div>
        </div>

        {/* Notes list */}
        <div className="p-4 space-y-3 overflow-auto flex-1 bg-white">
          {notes.length === 0 ? (
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
