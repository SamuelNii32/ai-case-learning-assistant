import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Button } from './ui/button'
import { FileText, Menu, X } from 'lucide-react'

export default function Header() {
  const [isMenuOpen, setIsMenuOpen] = useState(false)

  const toggleMenu = () => setIsMenuOpen(v => !v)

  return (
    <header className="sticky top-0 z-40 border-b border-slate-200 bg-white/80 backdrop-blur supports-[backdrop-filter]:bg-white/60">
      <div className="container mx-auto px-4 sm:px-6 h-16 flex items-center justify-between">
        {/* Logo */}
        <Link to="/" className="flex items-center gap-2">
          <div className="w-10 h-10 bg-[#125691] rounded-lg flex items-center justify-center">
            <FileText className="w-6 h-6 text-white" />
          </div>
          <span className="font-semibold text-lg sm:text-xl text-slate-900">AI Case Assistant</span>
        </Link>

        {/* Desktop Navigation */}
        <nav className="hidden md:flex items-center gap-8">
          <Link to="/about" className="text-slate-600 hover:text-slate-900 transition-colors">
            About
          </Link>
          <Link to="/privacy" className="text-slate-600 hover:text-slate-900 transition-colors">
            Privacy
          </Link>
          <Link to="/contact" className="text-slate-600 hover:text-slate-900 transition-colors">
            Contact
          </Link>
          <Link to="/login">
            <Button className="h-9 px-4 rounded-lg bg-[#125691] text-white hover:opacity-90">
              Sign In
            </Button>
          </Link>
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
              to="/about"
              className="block text-slate-600 hover:text-slate-900 transition-colors py-2"
              onClick={() => setIsMenuOpen(false)}
            >
              About
            </Link>
            <Link
              to="/privacy"
              className="block text-slate-600 hover:text-slate-900 transition-colors py-2"
              onClick={() => setIsMenuOpen(false)}
            >
              Privacy
            </Link>
            <Link
              to="/contact"
              className="block text-slate-600 hover:text-slate-900 transition-colors py-2"
              onClick={() => setIsMenuOpen(false)}
            >
              Contact
            </Link>

            <div className="pt-2">
              <Link to="/login" onClick={() => setIsMenuOpen(false)}>
                <Button variant="outline" className="w-full">
                  Sign In
                </Button>
              </Link>
            </div>
          </nav>
        </div>
      )}
    </header>
  )
}
