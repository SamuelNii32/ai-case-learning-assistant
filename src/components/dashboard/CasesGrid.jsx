import { Link, useNavigate } from 'react-router-dom'
import React, { useState } from 'react'
import { FileText, MoreVertical, Download, Edit2, Share, Trash2 } from 'lucide-react'
import { API_BASE } from '../../config'
import toast from 'react-hot-toast'
import { renameCase, deleteCase as apiDeleteCase, listSessionsMine, createSession, getPagedItems } from '@/lib/api'

// When clicking a case we prefer to open the user's most recent session for that uploadId.
// Only when the user explicitly chooses "New workspace" do we create a new session.
// This component implements that behavior: clicking the card opens the most recent session
// if one exists, otherwise it opens the workspace without selecting a session.

function SkeletonCard() {
  return (
    <div className="overflow-hidden rounded-2xl border border-[#e2d2c4] bg-[#fdfaf5] shadow-sm animate-pulse">
      <div className="aspect-[5/3] bg-[#f5ecde]" />
      <div className="p-5 space-y-3">
        <div className="h-5 w-3/4 bg-[#e0d0c2] rounded" />
        <div className="h-4 w-full bg-[#e0d0c2] rounded" />
        <div className="h-4 w-5/6 bg-[#e0d0c2] rounded" />
      </div>
    </div>
  )
}

export default function CasesGrid({ items = [], loading = false }) {
  const navigate = useNavigate()
  const [openId, setOpenId] = useState(null)
  const [renameOpen, setRenameOpen] = useState(false)
  const [renameTarget, setRenameTarget] = useState(null)
  const [renameValue, setRenameValue] = useState('')

  const [deleteOpen, setDeleteOpen] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState(null)

  if (loading) {
    return (
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {[...Array(6)].map((_, i) => (
          <SkeletonCard key={i} />
        ))}
      </div>
    )
  }

  if (!items.length) {
    return (
      <div className="col-span-full rounded-2xl border border-[#e2d2c4] bg-[#fffafa] p-8 text-center shadow-sm">
        <h3 className="text-[#2C2218] font-semibold">No cases match your filters</h3>
        <p className="text-[#5C4C3C] mt-1">Try clearing search or changing the search text.</p>
        <div className="mt-4">
          <Link
            to="/upload"
            className="inline-flex items-center gap-2 px-4 py-2 rounded-md bg-[#C96A08] text-white text-sm font-medium shadow-sm hover:bg-[#a05706] focus:outline-none focus:ring-2 focus:ring-[#C96A08]/60 focus:ring-offset-2 focus:ring-offset-[#f5ecde]"
          >
            Upload a Case
          </Link>
        </div>
      </div>
    )
  }

  const handleRename = c => {
    setRenameTarget(c)
    setRenameValue((c.title || c.name || '').replace(/\.pdf$/i, ''))
    setRenameOpen(true)
  }

  const performRename = async () => {
    if (!renameTarget) return
    const newTitle = (renameValue || '').trim()
    if (!newTitle) return
    try {
      await renameCase(renameTarget.id, newTitle)
      toast.success('Renamed')
      setRenameOpen(false)
      setRenameTarget(null)
      setOpenId(null)
      // Notify other parts of the app to refresh without a full page reload
      try {
        window.dispatchEvent(
          new CustomEvent('case:changed', {
            detail: { uploadId: renameTarget.id, action: 'rename' },
          })
        )
      } catch (e) {
        console.debug('Failed to dispatch case:changed', e)
      }
    } catch (err) {
      console.warn(err)
      toast.error('Rename failed')
    }
  }

  const handleShare = async c => {
    const appBase = import.meta.env.BASE_URL || '/'
    const url = `${window.location.origin}${appBase}workspace/${c.id}`
    try {
      await navigator.clipboard.writeText(url)
      toast.success('Link copied')
      setOpenId(null)
    } catch (e) {
      console.warn(e)
      toast.error('Copy failed')
    }
  }

  const handleDownload = c => {
    const base = API_BASE ? API_BASE.replace(/\/$/, '') : ''
    const href = `${base}/uploads/${c.id}/download`
    const a = document.createElement('a')
    a.href = href
    a.target = '_blank'
    a.rel = 'noopener'
    document.body.appendChild(a)
    a.click()
    a.remove()
    setOpenId(null)
  }

  const handleDelete = c => {
    setDeleteTarget(c)
    setDeleteOpen(true)
  }

  const performDelete = async () => {
    if (!deleteTarget) return
    try {
      await apiDeleteCase(deleteTarget.id)
      toast.success('Deleted')
      setDeleteOpen(false)
      setDeleteTarget(null)
      setOpenId(null)
      // Notify other parts of the app to refresh without a full page reload
      try {
        window.dispatchEvent(
          new CustomEvent('case:changed', {
            detail: { uploadId: deleteTarget.id, action: 'delete' },
          })
        )
      } catch (e) {
        console.debug('Failed to dispatch case:changed', e)
      }
    } catch (err) {
      console.warn(err)
      toast.error('Delete failed')
    }
  }

  function formatDate(s) {
    if (!s) return ''
    try {
      const d = new Date(s)
      if (isNaN(d)) return s
      return d.toLocaleDateString()
    } catch {
      return s
    }
  }

  function toTitleCase(str) {
    return String(str || '')
      .toLowerCase()
      .split(/\s+/)
      .map(w => (w ? w[0].toUpperCase() + w.slice(1) : ''))
      .join(' ')
      .trim()
  }

  async function handleOpenCase(c) {
    try {
      // Try to find most recent session for this uploadId for the current user
      const sessions = await listSessionsMine()
      const sessionItems = getPagedItems(sessions)
      if (sessionItems.length > 0) {
        const matches = sessionItems.filter(s => String(s.uploadId) === String(c.id))
        if (matches.length > 0) {
          // choose the most recent by lastActivityAt or createdAt
          matches.sort((a, b) => {
            const aTime = a.lastActivityAt ? new Date(a.lastActivityAt).getTime() : new Date(a.createdAt).getTime()
            const bTime = b.lastActivityAt ? new Date(b.lastActivityAt).getTime() : new Date(b.createdAt).getTime()
            return bTime - aTime
          })
          const recent = matches[0]
          try {
            toast.success('Resuming previous workspace')
          } catch {
            /* ignore */
          }
          navigate(`/workspace/${encodeURIComponent(c.id)}?sessionId=${encodeURIComponent(recent.sessionId || recent.id)}`)
          return
        }
      }
      // No existing sessions -> open workspace without a session (do not create session)
      navigate(`/workspace/${encodeURIComponent(c.id)}`)
    } catch (err) {
      console.error('[CasesGrid] failed to open case sessions', err)
      // fallback to workspace
      navigate(`/workspace/${encodeURIComponent(c.id)}`)
    }
  }

  async function handleNewWorkspace(c) {
    try {
      const created = await createSession(c.id)
      const sid = created?.sessionId || created?.id || null
      if (sid) {
        navigate(`/workspace/${encodeURIComponent(c.id)}?sessionId=${encodeURIComponent(sid)}`)
      } else {
        // fallback
        navigate(`/workspace/${encodeURIComponent(c.id)}`)
      }
    } catch (err) {
      console.error('[CasesGrid] failed to create session', err)
      toast.error('Failed to open new workspace')
    }
  }

  return (
    <>
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {items.map(c => (
          <div key={c.id} className="group">
            <div className="flex flex-col overflow-hidden rounded-2xl border border-[#e2d2c4] bg-white shadow-sm transition duration-200 ease-out transform hover:shadow-lg hover:-translate-y-1 relative">
              <div
                role="button"
                tabIndex={0}
                onClick={() => handleOpenCase(c)}
                className="group cursor-pointer"
              >
                <div className="h-36 w-full bg-[#f5ecde] flex items-center justify-center">
                  {c.image ? (
                    <img
                      src={c.image}
                      alt={c.title}
                      className="h-full w-full object-cover group-hover:scale-105 transition-transform duration-300"
                      loading="lazy"
                      decoding="async"
                      width={1000}
                      height={600}
                    />
                  ) : (
                    <div className="flex items-center justify-center w-full h-full text-[#C96A08]">
                      <FileText className="w-12 h-12" />
                    </div>
                  )}
                </div>
              </div>

              <div className="p-5 space-y-2 bg-white relative z-0">
                {(() => {
                  const fullTitle = (
                    c.title ||
                    c.name ||
                    c.fileName ||
                    c.filename ||
                    'Untitled PDF'
                  ).replace(/\.pdf$/i, '')
                  return (
                    <h3
                      title={fullTitle}
                      className="font-semibold text-lg text-[#2C2218] group-hover:text-[#C96A08] transition-colors truncate"
                    >
                      {toTitleCase(fullTitle)}
                    </h3>
                  )
                })()}

                {(() => {
                  const raw = c.fileName || c.filename || ''
                  const main = (c.title || c.name || '').replace(/\.pdf$/i, '')
                  if (raw && raw !== main) {
                    return <div className="text-xs text-[#8B7462]">{raw}</div>
                  }
                  return null
                })()}

                <p className="text-sm text-[#5C4C3C] leading-relaxed">{c.description}</p>

                <div className="pt-2">
                  <div className="text-xs text-[#8B7462]">
                    {formatDate(c.createdAt || c.uploadedAt || c.uploaded_at)}
                  </div>
                </div>
                <div className="pt-2" />
              </div>

              <div className="absolute bottom-3 right-3 z-50">
                <button
                  onClick={e => {
                    e.stopPropagation()
                    setOpenId(openId === c.id ? null : c.id)
                  }}
                  className="p-1 rounded-md text-[#5C4C3C] bg-white/90 hover:bg-[#fff2e4] border border-[#e2d2c4] cursor-pointer focus:outline-none focus:ring-2 focus:ring-[#C96A08]/50"
                  aria-label="Open actions"
                >
                  <MoreVertical className="w-5 h-5" />
                </button>

                {openId === c.id && (
                  <div className="absolute bottom-10 right-0 z-50 w-44 bg-white rounded-md shadow-lg border border-[#e2d2c4] py-1">
                    <button
                      onClick={() => {
                        handleRename(c)
                        setOpenId(null)
                      }}
                      className="w-full text-left px-3 py-2 text-sm text-[#2C2218] hover:bg-[#fff2e4] flex items-center gap-2 cursor-pointer"
                    >
                      <Edit2 className="w-4 h-4 text-[#C96A08]" /> Rename
                    </button>
                    <button
                      onClick={() => {
                        handleNewWorkspace(c)
                        setOpenId(null)
                      }}
                      className="w-full text-left px-3 py-2 text-sm text-[#2C2218] hover:bg-[#fff2e4] flex items-center gap-2 cursor-pointer"
                    >
                      <FileText className="w-4 h-4 text-[#C96A08]" /> New workspace
                    </button>
                    <button
                      onClick={() => {
                        handleShare(c)
                        setOpenId(null)
                      }}
                      className="w-full text-left px-3 py-2 text-sm text-[#2C2218] hover:bg-[#fff2e4] flex items-center gap-2 cursor-pointer"
                    >
                      <Share className="w-4 h-4 text-[#C96A08]" /> Share
                    </button>
                    <button
                      onClick={() => {
                        handleDownload(c)
                        setOpenId(null)
                      }}
                      className="w-full text-left px-3 py-2 text-sm text-[#2C2218] hover:bg-[#fff2e4] flex items-center gap-2 cursor-pointer"
                    >
                      <Download className="w-4 h-4 text-[#C96A08]" /> Download
                    </button>
                    <button
                      onClick={() => {
                        handleDelete(c)
                        setOpenId(null)
                      }}
                      className="w-full text-left px-3 py-2 text-sm text-red-600 hover:bg-[#fff2e4] flex items-center gap-2 cursor-pointer"
                    >
                      <Trash2 className="w-4 h-4 text-red-600" /> Delete
                    </button>
                  </div>
                )}
              </div>
            </div>
          </div>
        ))}
      </div>

      {renameOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div className="fixed inset-0 bg-black/40" onClick={() => setRenameOpen(false)} />
          <div className="bg-[#fffefa] rounded-2xl shadow-2xl p-6 w-full max-w-md mx-4 relative border border-[#e2d2c4]">
            <h3 className="text-lg font-semibold mb-2 text-[#2C2218]">Rename case</h3>
            <input
              className="w-full border border-[#d6c6b4] px-3 py-2 rounded mb-3 text-[#2C2218]"
              value={renameValue}
              onChange={e => setRenameValue(e.target.value)}
              aria-label="New case name"
            />
            <div className="flex justify-end gap-2">
              <button
                className="px-3 py-2 text-[#5C4C3C]"
                onClick={() => setRenameOpen(false)}
              >
                Cancel
              </button>
              <button
                className="px-3 py-2 bg-[#C96A08] text-white rounded shadow-sm"
                onClick={performRename}
              >
                Rename
              </button>
            </div>
          </div>
        </div>
      )}

      {deleteOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div className="fixed inset-0 bg-black/40" onClick={() => setDeleteOpen(false)} />
          <div className="bg-[#fffefa] rounded-2xl shadow-2xl p-6 w-full max-w-sm mx-4 relative border border-[#e2d2c4]">
            <h3 className="text-lg font-semibold mb-2 text-red-600">Delete case</h3>
            <p className="text-sm text-[#5C4C3C] mb-4">
              Are you sure you want to delete this case? This cannot be undone.
            </p>
            <div className="flex justify-end gap-2">
              <button className="px-3 py-2 text-[#5C4C3C]" onClick={() => setDeleteOpen(false)}>
                Cancel
              </button>
              <button className="px-3 py-2 bg-red-600 text-white rounded" onClick={performDelete}>
                Delete
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
