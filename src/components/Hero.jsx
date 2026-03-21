import React, { useContext } from 'react'
import { Link } from 'react-router-dom'
import { Button } from './ui/button'
import { AuthContext } from '@/contexts/AuthContext'

export function Hero() {
  const auth = useContext(AuthContext)
  const isLoggedIn = Boolean(auth?.loggedIn)

  return (
    <div className="mx-auto max-w-3xl flex flex-col items-center text-center space-y-10 py-16">
      <h1 className="text-3xl sm:text-4xl font-extrabold text-[#2c2218] leading-tight tracking-tight">
        <span className="block whitespace-nowrap">Master Complex Cases</span>
        <span className="block whitespace-nowrap text-[#2c2218]">
          <span className="font-medium">With</span>{' '}
          <span className="text-[#C96A08] font-semibold italic tracking-tight">AI Powered Learning</span>
        </span>
      </h1>
      <p className="mx-auto max-w-2xl text-lg text-[#5a3c25] leading-relaxed px-4 sm:px-0">
        Upload your own case studies or work through instructor-assigned cases with AI-guided
        analysis, notes, and evidence-based answers.
      </p>
      <div className="flex flex-col items-center gap-4 mt-6">
        {isLoggedIn ? (
          <div className="relative group inline-flex">
            <span className="pointer-events-none absolute inset-x-0 -bottom-2 h-4 rounded-[34px] bg-[#a34f10] transition-transform duration-200 group-hover:-translate-y-0.5"></span>

            <Link to="dashboard" className="relative inline-block">
              <Button
                size="lg"
                className="relative z-10 inline-flex h-[65px] items-center justify-center rounded-[34px] border-0 bg-[#e58a2a] px-12 py-0 text-[16px] font-black uppercase tracking-[0.04em] text-white no-underline shadow-none transition-transform duration-200 hover:bg-[#dc7f1d] hover:text-white hover:no-underline focus-visible:outline-none focus-visible:ring-0 focus-visible:ring-offset-0 active:bg-[#d47718] group-hover:-translate-y-0.5"
              >
                <span className="font-black underline decoration-white decoration-[1px] underline-offset-4">
                  OPEN YOUR WORKSPACE
                </span>
                <span className="ml-2 text-[24px] font-black leading-none">→</span>
              </Button>
            </Link>
          </div>
        ) : (
          <div className="relative group inline-flex">
            <span className="pointer-events-none absolute inset-x-0 -bottom-2 h-4 rounded-[34px] bg-[#a34f10] transition-transform duration-200 group-hover:-translate-y-0.5"></span>

            <Link to="login" className="relative inline-block">
              <Button
                size="lg"
                className="relative z-10 inline-flex h-[65px] items-center justify-center rounded-[34px] border-0 bg-[#e58a2a] px-12 py-0 text-[16px] font-black uppercase tracking-[0.04em] text-white no-underline shadow-none transition-transform duration-200 hover:bg-[#dc7f1d] hover:text-white hover:no-underline focus-visible:outline-none focus-visible:ring-0 focus-visible:ring-offset-0 active:bg-[#d47718] group-hover:-translate-y-0.5"
              >
                <span className="font-black underline decoration-white decoration-[1px] underline-offset-4">
                  OPEN YOUR WORKSPACE
                </span>
                <span className="ml-2 text-[24px] font-black leading-none">→</span>
              </Button>
            </Link>
          </div>
        )}
      </div>
    </div>
  )
}
