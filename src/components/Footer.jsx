import { Link } from "react-router-dom"
import { FileText } from "lucide-react"

export function Footer() {
  return (
    <footer className="bg-white border-t border-slate-200 py-12">
      <div className="container mx-auto px-6">
        <div className="grid md:grid-cols-4 gap-8 mb-8">
          <div className="space-y-4">
            <div className="flex items-center gap-2">
              <div className="w-8 h-8 bg-blue-600 rounded-lg flex items-center justify-center">
                <FileText className="w-5 h-5 text-white" />
              </div>
              <span className="font-semibold text-lg text-slate-900">AI Case Assistant</span>
            </div>
            <p className="text-sm text-slate-600">
              Empowering students and instructors with AI-powered case analysis and learning.
            </p>
          </div>

          <div>
            <h4 className="font-semibold text-slate-900 mb-4">Product</h4>
            <ul className="space-y-2">
              <li>
                <a href="#features" className="text-sm text-slate-600 hover:text-slate-900">
                  Features
                </a>
              </li>
              <li>
                <Link to="/dashboard" className="text-sm text-slate-600 hover:text-slate-900">
                  Try Demo
                </Link>
              </li>
              <li>
                <a href="#pricing" className="text-sm text-slate-600 hover:text-slate-900">
                  Pricing
                </a>
              </li>
            </ul>
          </div>

          <div>
            <h4 className="font-semibold text-slate-900 mb-4">Company</h4>
            <ul className="space-y-2">
              <li>
                <Link to="/about" className="text-sm text-slate-600 hover:text-slate-900">
                  About
                </Link>
              </li>
              <li>
                <Link to="/privacy" className="text-sm text-slate-600 hover:text-slate-900">
                  Privacy
                </Link>
              </li>
              <li>
                <Link to="/contact" className="text-sm text-slate-600 hover:text-slate-900">
                  Contact
                </Link>
              </li>
            </ul>
          </div>

          <div>
            <h4 className="font-semibold text-slate-900 mb-4">Resources</h4>
            <ul className="space-y-2">
              <li>
                <a href="#docs" className="text-sm text-slate-600 hover:text-slate-900">
                  Documentation
                </a>
              </li>
              <li>
                <a href="#support" className="text-sm text-slate-600 hover:text-slate-900">
                  Support
                </a>
              </li>
              <li>
                <a href="#blog" className="text-sm text-slate-600 hover:text-slate-900">
                  Blog
                </a>
              </li>
            </ul>
          </div>
        </div>

        <div className="border-t border-slate-200 pt-8 flex flex-col md:flex-row justify-between items-center gap-4">
          <p className="text-sm text-slate-600">© {new Date().getFullYear()} AI Case Assistant. All rights reserved.</p>
          <div className="flex items-center gap-6">
            <a href="#terms" className="text-sm text-slate-600 hover:text-slate-900">
              Terms
            </a>
            <Link to="/privacy" className="text-sm text-slate-600 hover:text-slate-900">
              Privacy Policy
            </Link>
            <a href="#cookies" className="text-sm text-slate-600 hover:text-slate-900">
              Cookies
            </a>
          </div>
        </div>
      </div>
    </footer>
  )
}