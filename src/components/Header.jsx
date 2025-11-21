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
    <header className="sticky top-0 z-40 border-b border-slate-200 bg-white/80 backdrop-blur supports-[backdrop-filter]:bg-white/60">
      <div className="container mx-auto px-4 sm:px-6 h-16 flex items-center justify-between">
        {/* Logo */}
        <Link to="/" className="flex items-center gap-2">
          <div className="w-10 h-10 bg-[#125691] rounded-lg flex items-center justify-center">
            <FileText className="w-6 h-6 text-white" />
          </div>
          {/* Always show the visible brand text to improve discoverability. If you
              prefer to hide it on the landing page to avoid duplicate headings,
              revert this to the prior isRoot conditional. */}
          <span className="font-semibold text-lg sm:text-xl text-slate-900">AI Case Assistant</span>
        </Link>

        {/* Desktop Navigation */}
        <nav className="hidden md:flex items-center gap-8">
          <Link to="about" className="text-slate-600 hover:text-slate-900 transition-colors">
            About
          </Link>
          <Link to="privacy" className="text-slate-600 hover:text-slate-900 transition-colors">
            Privacy
          </Link>
          <Link to="contact" className="text-slate-600 hover:text-slate-900 transition-colors">
            Contact
          </Link>
          {auth?.loggedIn ? (
            <div className="flex items-center gap-3 relative" ref={userMenuRef}>
              <button
                type="button"
                onClick={() => setUserMenuOpen(v => !v)}
                className="inline-flex items-center gap-2 h-9 px-3 rounded-md bg-slate-100 border border-slate-200 hover:bg-slate-200"
              >
                <span className="text-sm text-slate-900 font-medium">
                  {auth.user?.fullName || auth.user?.email || 'Account'}
                </span>
                <svg
                  className="w-3 h-3 text-slate-400"
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
                      to={auth?.user?.isSuperUser ? '/admin/sessions' : '/dashboard'}
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
              <Button className="h-9 px-4 rounded-lg bg-[#125691] text-white hover:opacity-90">
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
                  <Button
                    variant="outline"
                    className="w-full"
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
                  >
                    Sign out
                  </Button>
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
