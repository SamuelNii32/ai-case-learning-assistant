import React from 'react'
import { Link } from 'react-router-dom'
import { Button } from './ui/button'
import { PreviewMock } from './PreviewMock'
import { HowItWorks } from './HowItWorks'
import { ValueProps } from './ValueProps'

export function Hero() {
  return (
    <div className="space-y-16">
      {/* Hero Section - Side by Side Layout */}
      <div className="relative rounded-3xl p-8 lg:p-12 overflow-hidden">
        <div className="relative grid lg:grid-cols-2 gap-12 items-start">
          {/* Left Side - Hero Text + ValueProps */}
          <div className="space-y-6 text-left">
            <h1 className="text-4xl lg:text-5xl font-bold leading-tight">
              <span className="text-slate-900">Master Complex Cases with </span>
              <span className="text-[#125691]">AI-Powered Learning</span>
            </h1>
            <p className="text-xl text-gray-700 leading-relaxed">
              Upload a case, choose guided walkthrough or free Q&A, and get evidence-grounded
              answers with citations.
            </p>
            <div className="flex items-center gap-4">
              <Link to="/login">
                <Button size="lg" className="shadow-lg">
                  Sign In
                </Button>
              </Link>
              <Link to="/dashboard">
                <Button
                  size="lg"
                  variant="outline"
                  className="border-[#125691] text-[#125691] hover:bg-[#125691]/10 shadow-lg"
                >
                  Try Demo
                </Button>
              </Link>
            </div>

            {/* ValueProps - stays in left column, aligned with PreviewMock */}
            <div className="lg:pr-4">
              <ValueProps />
            </div>
          </div>

          {/* Right Side - Preview */}
          <div className="lg:pl-8">
            <PreviewMock />
          </div>
        </div>
      </div>

      {/* How It Works Section */}
      <HowItWorks />
    </div>
  )
}
