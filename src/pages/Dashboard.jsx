import { useState, useEffect } from 'react'
import { useOutletContext } from 'react-router-dom'
import FiltersBar from '../components/dashboard/FiltersBar'
import CasesGrid from '../components/dashboard/CasesGrid'
import SortControl from '../components/dashboard/SortControl'
import { Link } from 'react-router-dom'
import { Upload } from 'lucide-react'

export default function Dashboard() {
  // ⬇️ get search from layout
  const { searchQuery } = useOutletContext()

  const [activeFilter, setActiveFilter] = useState('all')
  const [sortDir, setSortDir] = useState('desc') 
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    // simulate fetching; delete this when you plug in real data
    const t = setTimeout(() => setLoading(false), 600)
    return () => clearTimeout(t)
  }, [])

  const mockCases = [
    {
      id: 1,
      title: 'Supply Chain Disruption Analysis',
      description: 'Analyze supplier performance and recommend mitigation strategies',
      image: '/supply-chain-logistics-shipping-containers.jpg',
      mode: 'Guided',
      status: 'in-progress',
      createdAt: '2025-10-05T14:30:00Z',
    },
    {
      id: 2,
      title: 'Market Entry Strategy',
      description: 'Evaluate market conditions for international expansion',
      image: '/global-market-analysis-world-map-data.jpg',
      mode: 'Free',
      status: 'in-progress',
      createdAt: '2025-10-08T09:15:00Z',
    },
    {
      id: 3,
      title: 'Financial Performance Review',
      description: 'Assess quarterly financial statements and identify trends',
      image: '/financial-charts-graphs-data-analysis.jpg',
      mode: 'Guided',
      status: 'completed',
      createdAt: '2025-10-02T16:45:00Z',
    },
  ]

  const filtered = mockCases.filter(c => {
    const matchesSearch = c.title.toLowerCase().includes((searchQuery || '').toLowerCase())
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
        </div>

        {/* Upload Case action (top-right) */}
        <Link
          to="/upload"
          className="inline-flex items-center gap-2 rounded-md bg-blue-600 px-3 py-2 text-sm font-medium 
               text-white hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2"
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
