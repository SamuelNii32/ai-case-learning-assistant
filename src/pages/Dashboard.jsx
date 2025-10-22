import React, { useState, useEffect } from 'react'
import { useOutletContext, Link } from 'react-router-dom'
import FiltersBar from '../components/dashboard/FiltersBar'
import CasesGrid from '../components/dashboard/CasesGrid'
import SortControl from '../components/dashboard/SortControl'
import { Upload } from 'lucide-react'
import { API_BASE } from '../config'

export default function Dashboard() {
  // get search from layout
  const { searchQuery } = useOutletContext()

  // API ping (debug)
  const [ping, setPing] = useState('…')
  const [cases, setCases] = useState([])

  // UI state
  const [activeFilter, setActiveFilter] = useState('all')
  const [sortDir, setSortDir] = useState('desc')
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    // ping the API base (helpful for local dev)
    if (!API_BASE) {
      setPing('VITE_API_BASE not set')
      return
    }
    fetch(`${API_BASE}/ping`)
      .then(r => r.text())
      .then(setPing)
      .catch(e => setPing(String(e)))

    setLoading(true)
    fetch(`${API_BASE}/cases`)
      .then(res => {
        if (!res.ok) throw new Error('Failed to fetch cases')
        return res.json()
      })
      .then(data => {
        console.log('fetched cases:', data)
        // normalize: prefer title, fallback to name or fileName
        const normalized = data.map(c => ({ ...c, title: c.title || c.name || c.fileName || c.filename }))
        setCases(normalized)
        setLoading(false)
      })
      .catch(err => {
        console.warn('Failed to fetch cases:', err)
        setLoading(false)
        setCases([])
      })
  }, [])

  const filtered = cases.filter(c => {
    const matchesSearch = c.title?.toLowerCase().includes((searchQuery || '').toLowerCase())
    const matchesFilter =
      activeFilter === 'all' ||
      (activeFilter === 'in-progress' && c.status === 'in-progress') ||
      (activeFilter === 'completed' && c.status === 'completed')
    return matchesSearch && matchesFilter
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
          <div className="text-xs text-slate-400 mt-1">API: {ping}</div>
        </div>

        {/* Upload Case action (top-right) */}
        <Link
          to="/upload"
          className="inline-flex items-center gap-2 rounded-md bg-[#125691] px-3 py-2 text-sm font-medium 
       text-white hover:bg-[#0f4f74] focus:outline-none focus:ring-2 focus:ring-[#125691]/60 focus:ring-offset-2"
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
