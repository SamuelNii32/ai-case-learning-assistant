import { Outlet, Link, useLocation } from 'react-router-dom'

export default function FocusLayout() {
  const { pathname } = useLocation()
  const hideWorkspaceLabel = pathname.startsWith('/workspace')

  return (
    <div className="min-h-screen bg-slate-50 flex flex-col">
      {/* Slim top bar with Back - hidden on workspace routes */}
      {!hideWorkspaceLabel && (
        <header className="h-14 border-b border-slate-200 bg-white flex items-center justify-between px-4">
          <Link
            to="/dashboard"
            className="text-sm px-3 py-1.5 rounded border border-slate-300 hover:bg-slate-100
                       focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2"
          >
            ← Back to Dashboard
          </Link>
          <div className="text-slate-700 text-sm">Workspace</div>
          <div /> {/* spacer */}
        </header>
      )}

      <main className="flex-1 overflow-auto">
        <Outlet />
      </main>
    </div>
  )
}
