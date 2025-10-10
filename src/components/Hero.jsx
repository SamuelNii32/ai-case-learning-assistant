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
      <div className="relative bg-gradient-to-br from-blue-50 via-indigo-50 to-purple-50 rounded-3xl p-8 lg:p-12 overflow-hidden">
        {/* Background decoration */}
        <div className="absolute top-0 right-0 w-72 h-72 bg-gradient-to-br from-blue-200/30 to-purple-200/30 rounded-full blur-3xl"></div>
        <div className="absolute bottom-0 left-0 w-64 h-64 bg-gradient-to-tr from-indigo-200/30 to-blue-200/30 rounded-full blur-3xl"></div>

        <div className="relative grid lg:grid-cols-2 gap-12 items-start">
          {/* Left Side - Hero Text + ValueProps */}
          <div className="space-y-6 text-left">
            <h1 className="text-4xl lg:text-5xl font-bold bg-gradient-to-r from-gray-900 via-blue-800 to-indigo-800 bg-clip-text text-transparent leading-tight">
              Analyze complex cases.
              <br />
              Learn with AI.
            </h1>
            <p className="text-xl text-gray-700 leading-relaxed">
              Upload a case, choose guided walkthrough or free Q&A, and get evidence-grounded
              answers with citations.
            </p>
            <div className="flex items-center gap-4">
              <Link to="/login">
                <Button
                  size="lg"
                  className="bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-700 hover:to-indigo-700 shadow-lg shadow-blue-500/25"
                >
                  Sign In
                </Button>
              </Link>
              <Link to="/dashboard">
                <Button
                  size="lg"
                  variant="outline"
                  className="border-blue-300 text-blue-700 hover:bg-blue-50 shadow-lg shadow-blue-500/10"
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
