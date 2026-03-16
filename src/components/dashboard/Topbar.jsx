import { useState, useRef, useEffect, useContext } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search, ChevronDown, User, Settings, HelpCircle, LogOut, Menu } from 'lucide-react'
import { isDemoModeEnabled, isDemoSessionActive, endDemoSession } from '@/auth/demoMode'
import toast from 'react-hot-toast'
import { AuthContext } from '@/contexts/AuthContext'

export default function Topbar({
  searchValue,
  onSearchChange,
  onMenuClick,
  inputId = 'global-search',
}) {
  const [isDropdownOpen, setIsDropdownOpen] = useState(false)
  const dropdownRef = useRef(null)
  const auth = useContext(AuthContext)
  const navigate = useNavigate()

  function getDisplayName() {
    try {
      return auth?.user?.fullName || auth?.user?.name || auth?.user?.email || 'Account'
    } catch {
      return 'Account'
    }
  }

  function getEmail() {
    try {
      return auth?.user?.email || ''
    } catch {
      return ''
    }
  }

  function getInitials(s) {
    const str = String(s || '').trim()
    if (!str) return 'A'
    // If it's an email, use local part
    const emailMatch = str.match(/^([^@]+)/)
    const source = emailMatch ? emailMatch[1] : str
    const parts = source.split(/\s+/).filter(Boolean)
    if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase()
    if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
    return source.slice(0, 2).toUpperCase()
  }

  // Close dropdown when clicking outside
  useEffect(() => {
    function handleClickOutside(e) {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target)) {
        setIsDropdownOpen(false)
      }
    }
    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

  // Close dropdown on Escape
  useEffect(() => {
    function onKey(e) {
      if (e.key === 'Escape') setIsDropdownOpen(false)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const handleLogout = () => {
    try {
      if (auth?.logout) auth.logout()
    } catch {
      /* ignore */
    }
    try {
      // Only clear dashboard-specific ephemeral keys, avoid wiping unrelated session data
      sessionStorage.removeItem('dashboard:search')
    } catch {
      /* ignore */
    }
    setIsDropdownOpen(false)
    // Use SPA navigation so AuthProvider and router update cleanly and send user to landing
    try {
      toast.success('Signed out')
    } catch {
      /* ignore */
    }
    navigate('/')
  }

  const handleExitDemo = () => {
    try {
      endDemoSession()
    } catch {
      /* ignore */
    }
    try {
      toast.success('Exited demo mode')
    } catch {
      /* ignore */
    }
    navigate('/login')
  }

  return (
    <header className="sticky top-0 z-30 h-14 md:h-16 border-b border-slate-200 bg-white/95 backdrop-blur supports-[backdrop-filter]:bg-white/70 flex items-center gap-4 px-4 md:px-6">
      {/* Hamburger Menu - Mobile + Tablet (0-1023px) */}
      <button
        onClick={onMenuClick}
        className="lg:hidden p-2 rounded-lg hover:bg-slate-100 transition-colors flex-shrink-0"
        aria-label="Toggle sidebar"
      >
        <Menu className="w-5 h-5 text-slate-600" />
      </button>

      {/* Search */}
      <div className="w-full max-w-md">
        <div className="relative">
          <div className="absolute inset-y-0 left-0 flex items-center pl-3 pointer-events-none">
            <Search className="w-4 h-4 text-[#8B7462]" />
          </div>
          <input
            id={inputId} /* enables "/" focus shortcut if you wire it */
            type="text"
            placeholder="Search cases…"
            value={searchValue}
            onChange={e => onSearchChange?.(e.target.value)}
            className="w-full h-10 rounded-md border border-[#d6c6b4] bg-white pl-10 pr-3 text-[#2C2218] placeholder-[#8B7462] focus:outline-none focus:ring-2 focus:ring-[#C96A08]/60 focus:ring-offset-2 focus:ring-offset-[#f5ecde]"
            aria-label="Search cases"
          />
        </div>
      </div>

      {/* Demo badge */}
      {isDemoModeEnabled() && isDemoSessionActive() && (
        <div className="ml-auto mr-2 px-2 py-1 text-xs rounded bg-yellow-100 text-yellow-800 border border-yellow-200">
          Demo Mode
        </div>
      )}
      {/* User dropdown */}
      <div className="relative ml-auto" ref={dropdownRef}>
        <button
          onClick={() => setIsDropdownOpen(v => !v)}
          className="flex items-center gap-2 md:gap-3 px-2 py-1 rounded-md hover:bg-slate-100 transition-colors focus:outline-none focus:ring-2 focus:ring-blue-300/60"
          aria-haspopup="menu"
          aria-expanded={isDropdownOpen}
          aria-controls="topbar-user-menu"
        >
          <div className="w-9 h-9 md:w-10 md:h-10 bg-[#C96A08] rounded-full grid place-items-center text-white font-semibold">
            {getInitials(getDisplayName())}
          </div>
          <span className="hidden sm:inline font-medium text-slate-900">{getDisplayName()}</span>
          <ChevronDown className="w-4 h-4 text-slate-400 hidden sm:inline" />
        </button>

        {isDropdownOpen && (
          <div
            id="topbar-user-menu"
            role="menu"
            className="absolute right-0 mt-2 w-56 md:w-64 bg-white rounded-lg shadow-lg border border-slate-200 py-2 z-50"
          >
            <div className="px-4 py-3 border-b border-slate-100">
              <p className="font-medium text-slate-900">{getDisplayName()}</p>
              {getEmail() ? <p className="text-sm text-slate-500 truncate">{getEmail()}</p> : null}
            </div>

            <div className="py-1">
              <button
                role="menuitem"
                className="w-full text-left px-4 py-2 text-sm text-slate-700 hover:bg-slate-100 flex items-center gap-3"
                onClick={() => {
                  setIsDropdownOpen(false)
                  navigate('settings')
                }}
              >
                <User className="w-4 h-4" />
                Profile Settings
              </button>
              <button
                role="menuitem"
                className="w-full text-left px-4 py-2 text-sm text-slate-700 hover:bg-slate-100 flex items-center gap-3"
                onClick={() => {
                  setIsDropdownOpen(false)
                  navigate('settings')
                }}
              >
                <Settings className="w-4 h-4" />
                Account Preferences
              </button>
              <button
                role="menuitem"
                className="w-full text-left px-4 py-2 text-sm text-slate-700 hover:bg-slate-100 flex items-center gap-3"
                onClick={() => {
                  setIsDropdownOpen(false)
                  navigate('contact')
                }}
              >
                <HelpCircle className="w-4 h-4" />
                Help & Support
              </button>

              <div className="border-t border-slate-100 my-1" />

              {isDemoModeEnabled() && isDemoSessionActive() && (
                <button
                  role="menuitem"
                  className="w-full text-left px-4 py-2 text-sm text-slate-700 hover:bg-slate-100 flex items-center gap-3"
                  onClick={handleExitDemo}
                >
                  <LogOut className="w-4 h-4" />
                  Exit Demo
                </button>
              )}

              <button
                onClick={handleLogout}
                role="menuitem"
                className="w-full text-left px-4 py-2 text-sm text-red-600 hover:bg-red-50 flex items-center gap-3"
              >
                <LogOut className="w-4 h-4" />
                Log Out
              </button>
            </div>
          </div>
        )}
      </div>
    </header>
  )
}
