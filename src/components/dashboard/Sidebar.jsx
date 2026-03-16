import { Link, NavLink } from 'react-router-dom'
import { useContext } from 'react'
import { FolderOpen, Clock, X, FileText, Settings, Users } from 'lucide-react' // ← icons
import { AuthContext } from '@/contexts/AuthContext'

export default function Sidebar({ isOpen = false, onClose = () => {} }) {
  const auth = useContext(AuthContext)
  const isInstructor = auth?.user?.role === 'instructor'

  const items = isInstructor
    ? [
        { to: '/admin/classes', label: 'Classes', icon: Users },
        { to: '/admin/sessions', label: 'History', icon: Clock },
      ]
    : [
        { to: '/dashboard', label: 'My Cases', icon: FolderOpen },
        { to: '/classes', label: 'My Classes', icon: Users },
        { to: '/session-history', label: 'Session History', icon: Clock },
      ]

  const base =
    'flex items-center gap-2 px-3 py-2 rounded-md text-sm transition flex-nowrap focus:outline-none focus:ring-2 focus:ring-[#C96A08]/50 focus:ring-offset-2 focus:ring-offset-[#f5ecde]'
  const active =
    'text-[#2C2218] bg-[#fffaf5] font-semibold border-l-4 border-[#C96A08] shadow-sm'
  const inactive = 'text-[#5C4C3C] hover:bg-[#f5ecde]'

  return (
    <aside
      className={`
      w-56 bg-[#f8f5ef] flex flex-col border-r border-[#e2d2c4]
      fixed lg:static inset-y-0 left-0 z-50
      transform transition-transform duration-300 ease-in-out
      ${isOpen ? 'translate-x-0' : '-translate-x-full lg:translate-x-0'}
    `}
    >
      {/* Brand */}
      <div className="flex items-center justify-between p-4 border-b border-[#e2d2c4] lg:justify-start">
        <Link
          to="/"
          className="flex items-center gap-2 font-semibold focus:outline-none focus:ring-2 focus:ring-[#C96A08]/60 focus:ring-offset-2 focus:ring-offset-[#f5ecde] rounded"
        >
          <div className="w-8 h-8 rounded-lg flex items-center justify-center bg-[#C96A08] shadow-inner">
            <FileText className="w-5 h-5 text-[#f8f5ef]" />
          </div>
          <span className="text-[#2C2218]">AI Case Assistant</span>
        </Link>
        <button
          onClick={onClose}
          className="lg:hidden p-1 rounded-md hover:bg-[#f5ecde] focus:outline-none focus:ring-2 focus:ring-[#C96A08]/60 focus:ring-offset-2 focus:ring-offset-[#f5ecde]"
          aria-label="Close sidebar"
        >
          <X className="w-5 h-5 text-[#2C2218]" />
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
                  end={to !== '/dashboard' && to !== 'dashboard'}
                  onClick={onClose}
                >
                  {({ isActive }) => (
                    <>
                      <Icon
                        className={`w-5 h-5 ${
                          isActive ? 'text-[#C96A08]' : 'text-[#5C4C3C]'
                        }`}
                        aria-hidden="true"
                      />
                      <span>{label}</span>
                    </>
                  )}
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
          className={({ isActive }) => `${base} ${isActive ? active : inactive}`}
          onClick={onClose}
        >
          {({ isActive }) => (
            <>
              <Settings
                className={`w-5 h-5 ${isActive ? 'text-[#C96A08]' : 'text-[#5C4C3C]'}`}
                aria-hidden="true"
              />
              <span>Settings</span>
            </>
          )}
        </NavLink>
      </div>
    </aside>
  )
}
