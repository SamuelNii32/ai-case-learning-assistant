import { Link } from 'react-router-dom'
import React, { useState } from 'react'
import { FileText, MoreVertical, Download, Edit2, Share, Trash2 } from 'lucide-react'
import { API_BASE } from '../../config'
import toast from 'react-hot-toast'
import { renameCase, deleteCase as apiDeleteCase } from '@/lib/api'

function SkeletonCard() {
  return (
    <div className="overflow-hidden rounded-lg border border-slate-200 bg-white shadow-sm animate-pulse">
      <div className="aspect-[5/3] bg-slate-100" />
      <div className="p-5 space-y-3">
        <div className="h-5 w-3/4 bg-slate-100 rounded" />
        <div className="h-4 w-full bg-slate-100 rounded" />
        <div className="h-4 w-5/6 bg-slate-100 rounded" />
      </div>
    </div>
  )
}

export default function CasesGrid({ items = [], loading = false }) {
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
      <div className="col-span-full rounded-lg border border-slate-200 bg-white p-8 text-center">
        <h3 className="text-slate-900 font-semibold">No cases match your filters</h3>
        <p className="text-slate-600 mt-1">Try clearing search or changing the status filter.</p>
        <div className="mt-4">
          <Link
            to="/upload"
            className="inline-flex items-center gap-2 px-4 py-2 rounded-md bg-[#125691] text-white text-sm hover:bg-[#0f4f74] focus:outline-none focus:ring-2 focus:ring-[#125691]/60 focus:ring-offset-2"
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
        window.dispatchEvent(new CustomEvent('case:changed', { detail: { uploadId: renameTarget.id, action: 'rename' } }))
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
        window.dispatchEvent(new CustomEvent('case:changed', { detail: { uploadId: deleteTarget.id, action: 'delete' } }))
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

  return (
    <>
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {items.map(c => (
          <div key={c.id} className="block group">
            <div className="overflow-hidden rounded-lg border border-slate-200 bg-white shadow-sm hover:shadow-md transition-shadow relative">
              <Link to={`/workspace/${c.id}`} className="block group">
                <div className="relative aspect-[5/3] overflow-hidden flex items-center justify-center bg-slate-100">
                  {c.image ? (
                    <img
                      src={c.image}
                      alt={c.title}
                      className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                      loading="lazy"
                      decoding="async"
                      width={1000}
                      height={600}
                    />
                  ) : (
                    <div className="flex items-center justify-center w-full h-full text-slate-400">
                      <FileText className="w-16 h-16" />
                    </div>
                  )}
                </div>

                <div className="p-5 space-y-2 relative z-0">
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
                        className="font-semibold text-lg text-slate-900 group-hover:text-[#125691] transition-colors truncate"
                      >
                        {toTitleCase(fullTitle)}
                      </h3>
                    )
                  })()}

                  {(() => {
                    const raw = c.fileName || c.filename || ''
                    const main = (c.title || c.name || '').replace(/\.pdf$/i, '')
                    if (raw && raw !== main) {
                      return <div className="text-xs text-slate-400">{raw}</div>
                    }
                    return null
                  })()}

                  <p className="text-sm text-slate-600 leading-relaxed">{c.description}</p>

                  <div className="pt-2">
                    <div className="text-xs text-slate-400">
                      {formatDate(c.createdAt || c.uploadedAt || c.uploaded_at)}
                    </div>
                  </div>
                  <div className="pt-2" />
                </div>
              </Link>

              <div className="absolute bottom-3 right-3 z-50">
                <button
                  onClick={e => {
                    e.stopPropagation()
                    setOpenId(openId === c.id ? null : c.id)
                  }}
                  className="p-1 rounded-md text-slate-600 bg-white/90 hover:bg-white border border-slate-200 cursor-pointer"
                  aria-label="Open actions"
                >
                  <MoreVertical className="w-5 h-5" />
                </button>

                {openId === c.id && (
                  <div className="absolute bottom-10 right-0 z-50 w-44 bg-white rounded-md shadow-lg border border-slate-200 py-1">
                    <button
                      onClick={() => {
                        handleRename(c)
                        setOpenId(null)
                      }}
                      className="w-full text-left px-3 py-2 text-sm hover:bg-slate-50 flex items-center gap-2 cursor-pointer"
                    >
                      <Edit2 className="w-4 h-4" /> Rename
                    </button>
                    <button
                      onClick={() => {
                        handleShare(c)
                        setOpenId(null)
                      }}
                      className="w-full text-left px-3 py-2 text-sm hover:bg-slate-50 flex items-center gap-2 cursor-pointer"
                    >
                      <Share className="w-4 h-4" /> Share
                    </button>
                    <button
                      onClick={() => {
                        handleDownload(c)
                        setOpenId(null)
                      }}
                      className="w-full text-left px-3 py-2 text-sm hover:bg-slate-50 flex items-center gap-2 cursor-pointer"
                    >
                      <Download className="w-4 h-4" /> Download
                    </button>
                    <button
                      onClick={() => {
                        handleDelete(c)
                        setOpenId(null)
                      }}
                      className="w-full text-left px-3 py-2 text-sm text-red-600 hover:bg-slate-50 flex items-center gap-2 cursor-pointer"
                    >
                      <Trash2 className="w-4 h-4" /> Delete
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
          <div className="bg-white rounded-lg shadow-lg p-4 w-full max-w-md mx-4 relative">
            <h3 className="text-lg font-semibold mb-2">Rename case</h3>
            <input
              className="w-full border px-3 py-2 rounded mb-3"
              value={renameValue}
              onChange={e => setRenameValue(e.target.value)}
              aria-label="New case name"
            />
            <div className="flex justify-end gap-2">
              <button className="px-3 py-2" onClick={() => setRenameOpen(false)}>
                Cancel
              </button>
              <button className="px-3 py-2 bg-[#125691] text-white rounded" onClick={performRename}>
                Rename
              </button>
            </div>
          </div>
        </div>
      )}

      {deleteOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div className="fixed inset-0 bg-black/40" onClick={() => setDeleteOpen(false)} />
          <div className="bg-white rounded-lg shadow-lg p-4 w-full max-w-sm mx-4 relative">
            <h3 className="text-lg font-semibold mb-2 text-red-600">Delete case</h3>
            <p className="text-sm text-slate-700 mb-4">
              Are you sure you want to delete this case? This cannot be undone.
            </p>
            <div className="flex justify-end gap-2">
              <button className="px-3 py-2" onClick={() => setDeleteOpen(false)}>
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
