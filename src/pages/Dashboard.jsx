import React, { useState, useEffect, Suspense, useContext } from 'react'
import { useOutletContext, Link, useNavigate } from 'react-router-dom'
import { AuthContext } from '@/contexts/AuthContext'
const FiltersBar = React.lazy(() => import('../components/dashboard/FiltersBar'))
const CasesGrid = React.lazy(() => import('../components/dashboard/CasesGrid'))
const SortControl = React.lazy(() => import('../components/dashboard/SortControl'))
import { Upload } from 'lucide-react'
import { listCases } from '@/lib/api'

export default function Dashboard() {
  // get search from layout
  const { searchQuery } = useOutletContext()
  const auth = useContext(AuthContext)
  const navigate = useNavigate()

  // If a superuser visits the regular dashboard, send them to the admin view
  useEffect(() => {
    if (auth?.user?.role === 'instructor') {
      navigate('/admin/sessions', { replace: true })
    }
  }, [auth?.user?.role, navigate])

  // API ping removed
  const [cases, setCases] = useState([])

  // UI state
  const [activeFilter, setActiveFilter] = useState('all')
  const [sortDir, setSortDir] = useState('desc')
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    // Defer the initial fetch until after first paint/idle so the header and
    // LCP text can render immediately. We still listen for case changes and
    // refresh when signalled.
    let cancelled = false

    async function fetchCasesAndNormalize() {
      // avoid calling protected endpoints when not authenticated
      if (!auth?.loggedIn) {
        setCases([])
        setLoading(false)
        return
      }
      setLoading(true)
      try {
        const data = await listCases()
        console.log('fetched cases:', data)

        const normalized = data.map(c => {
          const rawTitle =
            c.title || c.name || c.originalFileName || c.fileName || c.filename || c.uploadId
          const displayTitle = rawTitle
            ? String(rawTitle)
                .replace(/\.pdf$/i, '')
                .trim() || 'Untitled case'
            : 'Untitled case'
          return {
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

        if (!cancelled) setCases(normalized)
      } catch (err) {
        console.warn('Failed to fetch cases:', err)
        if (!cancelled) setCases([])
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    const schedule =
      typeof window !== 'undefined' && 'requestIdleCallback' in window
        ? window.requestIdleCallback(fetchCasesAndNormalize, { timeout: 500 })
        : window.setTimeout(fetchCasesAndNormalize, 100)

    const onUploaded = () => {
      try {
        fetchCasesAndNormalize()
      } catch (err) {
        console.warn('Failed to refresh cases on case:uploaded/case:changed', err)
      }
    }

    window.addEventListener('case:uploaded', onUploaded)
    window.addEventListener('case:changed', onUploaded)

    return () => {
      cancelled = true
      try {
        if (typeof window !== 'undefined' && 'cancelIdleCallback' in window)
          window.cancelIdleCallback(schedule)
        else clearTimeout(schedule)
      } catch (e) {
        console.debug('[Dashboard] idle/callback cancelled or failed', e)
      }
      window.removeEventListener('case:uploaded', onUploaded)
      window.removeEventListener('case:changed', onUploaded)
    }
  }, [auth?.loggedIn])

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
          <Suspense fallback={<div className="h-10 w-full bg-gray-100 rounded" />}>
            <FiltersBar active={activeFilter} onChange={setActiveFilter} />
          </Suspense>
          <Suspense fallback={<div className="h-10 w-32 bg-gray-100 rounded" />}>
            <SortControl dir={sortDir} onToggle={setSortDir} />
          </Suspense>
        </div>
        <Suspense fallback={<div className="py-8">Loading cases…</div>}>
          <CasesGrid items={sorted} loading={loading} />
        </Suspense>
      </div>
    </>
  )
}
