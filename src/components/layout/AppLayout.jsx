import { useEffect, useState } from 'react'
import { Outlet } from 'react-router-dom'
import Sidebar from '../dashboard/Sidebar'
import Topbar from '../dashboard/Topbar'

const KEY = 'dashboard:search'

export default function AppLayout() {
  const [searchQuery, setSearchQuery] = useState(() => sessionStorage.getItem(KEY) || '')
  const [sidebarOpen, setSidebarOpen] = useState(false)

  useEffect(() => {
    sessionStorage.setItem(KEY, searchQuery)
  }, [searchQuery])

  return (
    <div className="flex min-h-screen bg-slate-50">
      {/* Sidebar - hidden on mobile, always visible on lg+ */}
      <Sidebar isOpen={sidebarOpen} onClose={() => setSidebarOpen(false)} />

      {/* Mobile overlay */}
      {sidebarOpen && (
        <div
          className="fixed inset-0 bg-black bg-opacity-50 z-40 lg:hidden"
          onClick={() => setSidebarOpen(false)}
        />
      )}

      <div className="flex-1 flex flex-col">
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
