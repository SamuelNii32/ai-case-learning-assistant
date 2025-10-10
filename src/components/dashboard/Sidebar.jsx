import { Link, NavLink } from 'react-router-dom'
import { FolderOpen, Clock, X, FileText, Settings } from 'lucide-react' // ← icons

export default function Sidebar({ isOpen = false, onClose = () => {} }) {
  const items = [
    { to: '/dashboard', label: 'My Cases', icon: FolderOpen },
    { to: '/session-history', label: 'Session History', icon: Clock },
  ]

  const base =
    'flex items-center gap-2 px-3 py-2 rounded focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2'
  const active = 'bg-slate-200/70 text-slate-900 font-medium'
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
          <div className="w-8 h-8 bg-blue-600 rounded-lg flex items-center justify-center">
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
          {items.map(({ to, label, icon: Icon }) => (
            <li key={to}>
              <NavLink
                to={to}
                className={({ isActive }) => `${base} ${isActive ? active : inactive}`}
                end={to !== '/dashboard'}
              >
                <Icon className="w-5 h-5" aria-hidden="true" />
                <span>{label}</span>
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>

      {/* Settings - bottom pinned */}
      <div className="mt-auto p-3 border-t border-slate-200">
        <NavLink
          to="/settings"
          end
          className={({ isActive }) =>
            `flex items-center gap-2 px-3 py-2 rounded focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2 ${
              isActive ? 'bg-slate-200/70 text-slate-900 font-medium' : 'hover:bg-slate-100 text-slate-700'
            }`
          }
        >
          <Settings className="w-5 h-5" aria-hidden="true" />
          <span>Settings</span>
        </NavLink>
      </div>
    </aside>
  )
}
