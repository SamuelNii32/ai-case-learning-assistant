// Landing page was added earlier; placeholder to avoid runtime errors if referenced.
import React, { useContext } from 'react'
import { Link } from 'react-router-dom'
import { AuthContext } from '@/contexts/AuthContext'

export default function Landing() {
  const auth = useContext(AuthContext)

  if (import.meta.env.DEV) {
    try {
      console.debug('[Landing] auth', {
        loggedIn: auth?.loggedIn,
        token: auth?.token,
        ls: typeof window !== 'undefined' ? localStorage.getItem('authToken') : null,
      })
    } catch {
      /* ignore */
    }
  }

  return (
    <main className="min-h-screen flex items-center justify-center bg-slate-50">
      <div className="max-w-3xl mx-auto p-8 text-center">
        <h1 className="text-4xl font-bold text-slate-900 mb-4">AI Case Assistant</h1>
        <p className="text-lg text-slate-600 mb-6">
          Upload case studies and get AI-powered analysis and guided insights.
        </p>

        {auth?.loggedIn ? (
          <div className="space-y-3">
            <p className="text-slate-700">
              Welcome back{auth.user?.fullName ? `, ${auth.user.fullName}` : ''}.
            </p>
            <Link to="dashboard">
              <button className="px-6 py-3 rounded-md bg-[#125691] text-white">
                Open Dashboard
              </button>
            </Link>
          </div>
        ) : (
          <div className="space-y-3">
            <Link to="login">
              <button className="px-6 py-3 rounded-md bg-[#125691] text-white">Sign In</button>
            </Link>
            <Link to="/signup" className="block text-sm text-slate-600 hover:underline">
              Create an account
            </Link>
          </div>
        )}
      </div>
    </main>
  )
}
