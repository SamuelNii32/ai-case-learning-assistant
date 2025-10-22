import { useState, useRef, useEffect } from 'react'
import { Search, ChevronDown, User, Settings, HelpCircle, LogOut, Menu } from 'lucide-react'

export default function Topbar({
  searchValue,
  onSearchChange,
  onMenuClick,
  inputId = 'global-search',
}) {
  const [isDropdownOpen, setIsDropdownOpen] = useState(false)
  const dropdownRef = useRef(null)

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
    sessionStorage.clear()
    window.location.href = '/login'
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
      <div className="w-full max-w-sm md:max-w-md">
        <div className="relative">
          <div className="absolute inset-y-0 left-0 flex items-center pl-3 pointer-events-none">
            <Search className="w-4 h-4 text-slate-400" />
          </div>
          <input
            id={inputId} /* enables "/" focus shortcut if you wire it */
            type="text"
            placeholder="Search cases…"
            value={searchValue}
            onChange={e => onSearchChange?.(e.target.value)}
            className="w-full h-10 rounded-md border border-slate-300 pl-10 pr-3 text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-blue-300/60 focus:ring-offset-2"
            aria-label="Search cases"
          />
        </div>
      </div>

      {/* User dropdown */}
      <div className="relative ml-auto" ref={dropdownRef}>
        <button
          onClick={() => setIsDropdownOpen(v => !v)}
          className="flex items-center gap-2 md:gap-3 px-2 py-1 rounded-md hover:bg-slate-100 transition-colors focus:outline-none focus:ring-2 focus:ring-blue-300/60"
          aria-haspopup="menu"
          aria-expanded={isDropdownOpen}
          aria-controls="topbar-user-menu"
        >
          <div className="w-9 h-9 md:w-10 md:h-10 bg-[#125691] rounded-full grid place-items-center text-white font-semibold">
            SC
          </div>
          <span className="hidden sm:inline font-medium text-slate-900">Sarah Chen</span>
          <ChevronDown className="w-4 h-4 text-slate-400 hidden sm:inline" />
        </button>

        {isDropdownOpen && (
          <div
            id="topbar-user-menu"
            role="menu"
            className="absolute right-0 mt-2 w-56 md:w-64 bg-white rounded-lg shadow-lg border border-slate-200 py-2 z-50"
          >
            <div className="px-4 py-3 border-b border-slate-100">
              <p className="font-medium text-slate-900">Sarah Chen</p>
              <p className="text-sm text-slate-500 truncate">sarah.chen@example.com</p>
            </div>

            <div className="py-1">
              <button
                role="menuitem"
                className="w-full text-left px-4 py-2 text-sm text-slate-700 hover:bg-slate-100 flex items-center gap-3"
              >
                <User className="w-4 h-4" />
                Profile Settings
              </button>
              <button
                role="menuitem"
                className="w-full text-left px-4 py-2 text-sm text-slate-700 hover:bg-slate-100 flex items-center gap-3"
              >
                <Settings className="w-4 h-4" />
                Account Preferences
              </button>
              <button
                role="menuitem"
                className="w-full text-left px-4 py-2 text-sm text-slate-700 hover:bg-slate-100 flex items-center gap-3"
              >
                <HelpCircle className="w-4 h-4" />
                Help & Support
              </button>

              <div className="border-t border-slate-100 my-1" />

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
