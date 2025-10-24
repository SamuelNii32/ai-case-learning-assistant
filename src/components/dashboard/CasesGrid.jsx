import { Link } from 'react-router-dom'
import React, { useState } from 'react'
import { FileText, MoreVertical, Download, Edit2, Share, Trash2 } from 'lucide-react'
import { API_BASE } from '../../config'
import toast from 'react-hot-toast'

function statusClasses(status) {
  if (status === 'completed') return 'bg-emerald-50 text-emerald-700 border border-emerald-200'
  return 'bg-amber-50 text-amber-700 border border-amber-200'
}

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

  const handleRename = async c => {
    const newTitle = window.prompt('Rename PDF', (c.title || '').replace(/\.pdf$/i, ''))
    if (!newTitle) return
    try {
      const res = await fetch(`${API_BASE}/cases/${c.id}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          title: newTitle + (c.title && c.title.match(/\.pdf$/i) ? '.pdf' : ''),
        }),
      })
      if (!res.ok) throw new Error('Rename failed')
      toast.success('Renamed')
      setTimeout(() => window.location.reload(), 300)
    } catch (e) {
      console.warn(e)
      toast.error('Rename failed')
    }
  }

  const handleShare = async c => {
    const url = `${window.location.origin}${window.location.pathname}workspace/${c.id}`
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
    const href = `${API_BASE}/cases/${c.id}/download`
    const a = document.createElement('a')
    a.href = href
    a.target = '_blank'
    a.rel = 'noopener'
    document.body.appendChild(a)
    a.click()
    a.remove()
    setOpenId(null)
  }

  const handleDelete = async c => {
    if (!window.confirm('Delete this case? This cannot be undone.')) return
    try {
      const res = await fetch(`${API_BASE}/cases/${c.id}`, { method: 'DELETE' })
      if (!res.ok) throw new Error('Delete failed')
      toast.success('Deleted')
      setTimeout(() => window.location.reload(), 200)
    } catch (e) {
      console.warn(e)
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

                {/* Status badge placed on the cover image (top-right) */}
                <span
                  className={`absolute top-3 right-3 z-40 px-3 py-1 rounded-full text-xs font-medium ${statusClasses(c.status)}`}
                >
                  {c.status === 'completed' ? 'Completed' : 'In Progress'}
                </span>
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

            {/* Move actions button down to bottom-right of card; menu is absolutely positioned so it won't shift the button */}
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
  )
}
