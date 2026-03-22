import { useState, useContext, useRef, useEffect } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import toast from 'react-hot-toast'
import { Button } from './ui/button'
import { FileText, Menu, X } from 'lucide-react'
import { AuthContext } from '@/contexts/AuthContext'

export default function Header() {
  const [isMenuOpen, setIsMenuOpen] = useState(false)

  const auth = useContext(AuthContext)
  const navigate = useNavigate()
  const userMenuRef = useRef(null)
  const [userMenuOpen, setUserMenuOpen] = useState(false)

  // close user menu when clicking outside
  useEffect(() => {
    function onDocClick(e) {
      if (!userMenuRef.current) return
      if (!userMenuRef.current.contains(e.target)) setUserMenuOpen(false)
    }
    if (userMenuOpen) document.addEventListener('mousedown', onDocClick)
    return () => document.removeEventListener('mousedown', onDocClick)
  }, [userMenuOpen])
  if (import.meta.env.DEV) {
    try {
      console.debug('[Header] auth', {
        loggedIn: auth?.loggedIn,
        token: auth?.token,
        ls: typeof window !== 'undefined' ? localStorage.getItem('authToken') : null,
      })
    } catch {
      /* ignore */
    }
  }

  const toggleMenu = () => setIsMenuOpen(v => !v)

  return (
    <header className="sticky top-0 z-40 bg-[#fdfcf7] shadow-[0_10px_25px_rgba(0,0,0,0.08)] border-b border-slate-200 rounded-b-[28px] text-[#2c2218]">
      <div className="container mx-auto px-6 sm:px-10 py-4 flex items-center justify-between gap-6">
        {/* Logo */}
        <Link to="/" className="flex items-center gap-3">
          <img src="/fav.png" alt="CasePilot logo" className="w-8 h-8 object-contain" />
          <span className="font-semibold text-lg sm:text-xl text-[#1f140a]">CasePilot</span>
        </Link>

        {/* Desktop Navigation */}
        <nav className="hidden md:flex items-center gap-10 text-[#1f140a]">
          <Link
            to="about"
            className="text-base font-medium tracking-wide text-[#2c2218] transition-colors duration-200 hover:text-[#4a3a2a]"
          >
            About
          </Link>
          <Link
            to="privacy"
            className="text-base font-medium tracking-wide text-[#2c2218] transition-colors duration-200 hover:text-[#4a3a2a]"
          >
            Privacy
          </Link>
          <Link
            to="contact"
            className="text-base font-medium tracking-wide text-[#2c2218] transition-colors duration-200 hover:text-[#4a3a2a]"
          >
            Contact
          </Link>
          {auth?.loggedIn ? (
            <div className="flex items-center gap-3 relative" ref={userMenuRef}>
              <button
                type="button"
                onClick={() => setUserMenuOpen(v => !v)}
                className="inline-flex items-center gap-2 h-12 rounded-[10px] bg-[#e58a2a] px-4 text-sm font-semibold text-white transition-colors duration-200 hover:bg-[#dc7f1d]"
              >
                <span className="text-sm text-white font-semibold">
                  {auth.user?.fullName || auth.user?.email || 'Account'}
                </span>
                <svg
                  className="w-3 h-3 text-white"
                  viewBox="0 0 20 20"
                  fill="currentColor"
                  aria-hidden
                >
                  <path
                    fillRule="evenodd"
                    d="M5.23 7.21a.75.75 0 011.06.02L10 10.94l3.71-3.71a.75.75 0 111.06 1.06l-4.24 4.24a.75.75 0 01-1.06 0L5.21 8.29a.75.75 0 01.02-1.08z"
                    clipRule="evenodd"
                  />
                </svg>
              </button>

              {userMenuOpen && (
                <div className="absolute right-0 top-full mt-2 w-44 bg-white border border-slate-200 rounded-md shadow-lg z-50">
                  <div className="py-1">
                    <Link
                      to={auth?.user?.role === 'instructor' ? '/admin/sessions' : '/dashboard'}
                      className="block px-4 py-2 text-sm text-slate-700 hover:bg-slate-50"
                      onClick={() => setUserMenuOpen(false)}
                    >
                      Dashboard
                    </Link>
                    <Link
                      to="settings"
                      className="block px-4 py-2 text-sm text-slate-700 hover:bg-slate-50"
                      onClick={() => setUserMenuOpen(false)}
                    >
                      Settings
                    </Link>
                    <button
                      type="button"
                      onClick={() => {
                        try {
                          if (auth?.logout) auth.logout()
                        } catch {
                          /* ignore */
                        }
                        setUserMenuOpen(false)
                        try {
                          toast.success('Signed out')
                        } catch {
                          /* ignore */
                        }
                        navigate('/')
                      }}
                      className="w-full text-left px-4 py-2 text-sm text-slate-700 hover:bg-slate-50"
                    >
                      Sign out
                    </button>
                  </div>
                </div>
              )}
            </div>
          ) : (
            <Link to="login">
              <Button className="h-10 px-5 rounded-full bg-[#C96A08] text-white hover:bg-[#9c5306] transition-colors">
                Sign In
              </Button>
            </Link>
          )}
        </nav>

        {/* Mobile Hamburger Button */}
        <button
          onClick={toggleMenu}
          className="md:hidden p-2 rounded-lg hover:bg-gray-100 transition-colors"
          aria-label="Toggle menu"
        >
          {isMenuOpen ? (
            <X className="w-6 h-6 text-slate-600" />
          ) : (
            <Menu className="w-6 h-6 text-slate-600" />
          )}
        </button>
      </div>

      {/* Mobile Navigation Menu */}
      {isMenuOpen && (
        <div className="md:hidden bg-white border-t border-slate-200">
          <nav className="container mx-auto px-4 py-4 space-y-4">
            <Link
              to="about"
              className="block text-slate-600 hover:text-slate-900 transition-colors py-2"
              onClick={() => setIsMenuOpen(false)}
            >
              About
            </Link>
            <Link
              to="privacy"
              className="block text-slate-600 hover:text-slate-900 transition-colors py-2"
              onClick={() => setIsMenuOpen(false)}
            >
              Privacy
            </Link>
            <Link
              to="contact"
              className="block text-slate-600 hover:text-slate-900 transition-colors py-2"
              onClick={() => setIsMenuOpen(false)}
            >
              Contact
            </Link>

            <div className="pt-2">
              {auth?.loggedIn ? (
                <div className="space-y-2">
                  <div className="px-2">
                    <div className="text-sm text-slate-700 mb-2">
                      {auth.user?.fullName || auth.user?.email}
                    </div>
                  </div>
                  <Link to="settings" onClick={() => setIsMenuOpen(false)}>
                    <Button variant="outline" className="w-full">
                      Settings
                    </Button>
                  </Link>
                  <button
                    type="button"
                    onClick={() => {
                      try {
                        if (auth?.logout) auth.logout()
                      } catch {
                        /* ignore */
                      }
                      setIsMenuOpen(false)
                      try {
                        toast.success('Signed out')
                      } catch {
                        /* ignore */
                      }
                      navigate('/')
                    }}
                    className="inline-flex items-center gap-2 rounded-full bg-[#e58a2a] text-white px-4 py-2 transition-colors duration-200 hover:bg-[#dc7f1d]"
                  >
                    Sign out
                  </button>
                </div>
              ) : (
                <Link to="login" onClick={() => setIsMenuOpen(false)}>
                  <Button variant="outline" className="w-full">
                    Sign In
                  </Button>
                </Link>
              )}
            </div>
          </nav>
        </div>
      )}
    </header>
  )
}
