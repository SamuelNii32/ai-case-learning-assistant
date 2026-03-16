import React, { useContext } from 'react'
import { Link } from 'react-router-dom'
import { Button } from './ui/button'
import { AuthContext } from '@/contexts/AuthContext'

export function Hero() {
  const auth = useContext(AuthContext)
  const isLoggedIn = Boolean(auth?.loggedIn)

  return (
    <div className="mx-auto max-w-2xl flex flex-col items-center text-center space-y-6 py-16">
      <h1 className="text-4xl sm:text-5xl font-bold text-[#2c2218] leading-tight">
        Master Complex Cases with <span className="text-[#C96A08]">AI-Guided</span> Learning
      </h1>
      <p className="text-lg text-[#4a3b2c] leading-relaxed px-4 sm:px-0">
        Upload your own case studies or work through instructor-assigned cases with AI-guided
        analysis, notes, and evidence-based answers.
      </p>
      <div className="flex flex-col items-center gap-4">
        {isLoggedIn ? (
          <Link to="dashboard">
            <Button size="lg" className="bg-[#C96A08] text-white rounded-full px-10 py-3 hover:bg-[#9c5306]">
              Open Dashboard
            </Button>
          </Link>
        ) : (
          <Link to="login">
            <Button size="lg" className="bg-[#C96A08] text-white rounded-full px-10 py-3 hover:bg-[#9c5306]">
              Sign In
            </Button>
          </Link>
        )}
      </div>
    </div>
  )
}
