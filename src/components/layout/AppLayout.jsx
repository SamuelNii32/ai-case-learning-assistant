import { useEffect, useState } from 'react'
import { Outlet } from 'react-router-dom'
import Sidebar from '../dashboard/Sidebar'
import Topbar from '../dashboard/Topbar'
import SessionHistory from '../../pages/SessionHistory'
const KEY = 'dashboard:search'

export default function AppLayout() {
  const [searchQuery, setSearchQuery] = useState(() => sessionStorage.getItem(KEY) || '')
  const [sidebarOpen, setSidebarOpen] = useState(false)

  useEffect(() => {
    sessionStorage.setItem(KEY, searchQuery)
  }, [searchQuery])

  return (
    <div className="relative flex min-h-screen overflow-x-hidden bg-slate-50">
      {/* Sidebar - hidden on mobile, always visible on lg+ */}
      <Sidebar isOpen={sidebarOpen} onClose={() => setSidebarOpen(false)} />

      {/* Mobile overlay */}
      {sidebarOpen && (
        <div
          className="fixed inset-0 z-40 bg-slate-900/20 backdrop-blur-[1px] lg:hidden"
          onClick={() => setSidebarOpen(false)}
        />
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <Topbar
          searchValue={searchQuery}
          onSearchChange={setSearchQuery}
          onMenuClick={() => setSidebarOpen(true)}
        />
        <main className="flex-1 p-4 md:p-8">
          <Outlet context={{ searchQuery, setSearchQuery }} />
        </main>
      </div>
    </div>
  )
}
