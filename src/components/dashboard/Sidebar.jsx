import { Link, NavLink } from 'react-router-dom'
import { FolderOpen, Clock, X, FileText, Settings } from 'lucide-react' // ← icons

export default function Sidebar({ isOpen = false, onClose = () => {} }) {
  const items = [
    { to: 'dashboard', label: 'My Cases', icon: FolderOpen },
    { to: 'session-history', label: 'Session History', icon: Clock },
  ]

  const base =
    'flex items-center gap-2 px-3 py-2 rounded focus:outline-none focus:ring-2 focus:ring-offset-2'
  const active = 'text-white font-medium'
  const inactive = 'hover:bg-slate-100 text-slate-700'

  return (
    <aside
      className={`
      w-56 border-r border-slate-200 bg-white flex flex-col
      fixed lg:static inset-y-0 left-0 z-50
      transform transition-transform duration-300 ease-in-out
      ${isOpen ? 'translate-x-0' : '-translate-x-full lg:translate-x-0'}
    `}
    >
      {/* Brand */}
      <div className="flex items-center justify-between p-4 border-b border-slate-200 lg:justify-start">
        <Link
          to="/"
          className="flex items-center gap-2 font-semibold focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2 rounded"
        >
          <div
            className="w-8 h-8 rounded-lg flex items-center justify-center"
            style={{ backgroundColor: '#003c71' }}
          >
            <FileText className="w-5 h-5 text-white" />
          </div>
          <span>AI Case Assistant</span>
        </Link>
        <button
          onClick={onClose}
          className="lg:hidden p-1 rounded-md hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-300/60"
          aria-label="Close sidebar"
        >
          <X className="w-5 h-5" />
        </button>
      </div>

      {/* Nav */}
      <nav aria-label="Primary" className="p-3">
        <ul className="space-y-1" role="list">
          {items.map(({ to, label, icon }) => {
            const Icon = icon
            return (
              <li key={to}>
                <NavLink
                  to={to}
                  className={({ isActive }) => `${base} ${isActive ? active : inactive}`}
                  style={({ isActive }) =>
                    isActive ? { backgroundColor: '#003c71', borderLeft: `4px solid #deb406` } : {}
                  }
                  end={to !== 'dashboard'}
                  onClick={onClose}
                >
                  <Icon className="w-5 h-5" aria-hidden="true" />
                  <span>{label}</span>
                </NavLink>
              </li>
            )
          })}
        </ul>
      </nav>

      {/* Settings - bottom pinned */}
      <div className="mt-auto p-3 border-t border-slate-200">
        <NavLink
          to="settings"
          end
          className={({ isActive }) =>
            `flex items-center gap-2 px-3 py-2 rounded focus:outline-none focus:ring-2 focus:ring-offset-2 ${
              isActive ? 'text-white font-medium' : 'hover:bg-slate-100 text-slate-700'
            }`
          }
          style={({ isActive }) =>
            isActive ? { backgroundColor: '#003c71', borderLeft: `4px solid #deb406` } : {}
          }
          onClick={onClose}
        >
          <Settings className="w-5 h-5" aria-hidden="true" />
          <span>Settings</span>
        </NavLink>
      </div>
    </aside>
  )
}
