import React, { useState, useEffect } from 'react'
import { useOutletContext, Link } from 'react-router-dom'
import FiltersBar from '../components/dashboard/FiltersBar'
import CasesGrid from '../components/dashboard/CasesGrid'
import SortControl from '../components/dashboard/SortControl'
import { Upload } from 'lucide-react'
import { listCases } from '@/lib/api'

export default function Dashboard() {
  // get search from layout
  const { searchQuery } = useOutletContext()

  // API ping removed
  const [cases, setCases] = useState([])

  // UI state
  const [activeFilter, setActiveFilter] = useState('all')
  const [sortDir, setSortDir] = useState('desc')
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    // helper: fetch cases and normalize
    async function fetchCases() {
      setLoading(true)
      try {
        const data = await listCases()
        console.log('fetched cases:', data)

        const normalized = data.map(c => {
          // pick a raw title from whatever the backend sent
          const rawTitle =
            c.title || c.name || c.originalFileName || c.fileName || c.filename || c.uploadId

          // clean it up for display:
          // - strip .pdf
          // - trim whitespace
          const displayTitle = rawTitle
            ? String(rawTitle)
                .replace(/\.pdf$/i, '')
                .trim() || 'Untitled case'
            : 'Untitled case'

          return {
            // normalize server shape -> client shape expected by CasesGrid
            id: c.uploadId || c.id,
            uploadId: c.uploadId || c.id,
            fileName: c.originalFileName || c.fileName || c.filename,
            title: displayTitle,
            createdAt: c.createdAt || c.uploadedAt || c.uploaded_at,
            status: c.status || 'completed',
            description: c.description || '',
            image: c.image || null,
            ...c,
          }
        })

        setCases(normalized)
      } catch (err) {
        console.warn('Failed to fetch cases:', err)
        setCases([])
      } finally {
        setLoading(false)
      }
    }

    fetchCases()

    // listen for uploads or other case changes and refresh
    const onUploaded = () => {
      try {
        // Refresh full list when an upload completes or cases change
        fetchCases()
      } catch (err) {
        console.warn('Failed to refresh cases on case:uploaded/case:changed', err)
      }
    }

    window.addEventListener('case:uploaded', onUploaded)
    window.addEventListener('case:changed', onUploaded)
    return () => {
      window.removeEventListener('case:uploaded', onUploaded)
      window.removeEventListener('case:changed', onUploaded)
    }
  }, [])

  const filtered = cases.filter(c => {
    const matchesSearch = c.title?.toLowerCase().includes((searchQuery || '').toLowerCase())
    // Status filtering disabled for now — always include item based on search only
    return matchesSearch
  })

  const sorted = [...filtered].sort((a, b) => {
    const dateA = Date.parse(a.createdAt)
    const dateB = Date.parse(b.createdAt)
    return sortDir === 'desc' ? dateB - dateA : dateA - dateB
  })

  return (
    <>
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-slate-900">My Cases</h1>
          <p className="text-slate-600 mt-1">Continue your learning journey or start a new case</p>
        </div>

        {/* Upload Case action (top-right) */}
        <Link
          to="/upload"
          className="inline-flex items-center gap-2 rounded-md bg-[#125691] px-3 py-2 text-sm font-medium text-white hover:bg-[#0f4f74] focus:outline-none focus:ring-2 focus:ring-[#125691]/60 focus:ring-offset-2"
        >
          <Upload className="w-4 h-4" aria-hidden="true" />
          <span className="hidden sm:inline">Upload Case</span>
          <span className="sm:hidden">Upload</span>
        </Link>
      </div>

      <div className="mt-6 space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <FiltersBar active={activeFilter} onChange={setActiveFilter} />
          <SortControl dir={sortDir} onToggle={setSortDir} />
        </div>
        <CasesGrid items={sorted} loading={loading} />
      </div>
    </>
  )
}
