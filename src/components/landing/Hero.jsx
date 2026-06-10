import React from 'react'
import { Sparkles } from 'lucide-react'

export default function Hero() {
  return (
    <section className="bg-white text-slate-900 py-24">
      <div className="container mx-auto px-4">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-8 items-center">
          <div>
            <div className="inline-flex items-center gap-3 mb-6">
              <div className="w-9 h-9 bg-white/10 rounded-md flex items-center justify-center">
                <Sparkles className="w-5 h-5 text-white" />
              </div>
              <span className="text-sm opacity-90">Introducing CasePilot</span>
            </div>

            <h1 className="text-4xl md:text-5xl font-bold leading-tight mb-6">
              Turn case studies into insights with AI
            </h1>
            <p className="text-lg text-white/90 mb-6">
              Upload your PDF, let our AI extract structure and insights, and jump straight into
              analysis.
            </p>

            <div className="flex gap-3">
              <a
                className="inline-flex items-center bg-white text-brand px-5 py-3 rounded-md font-semibold shadow hover:bg-white/90"
                href="#get-started"
              >
                Get started
              </a>
              <a
                className="inline-flex items-center border-2 border-gold text-white/90 px-5 py-3 rounded-md font-semibold hover:border-gold-dark"
                href="#learn-more"
              >
                Learn more
              </a>
            </div>
          </div>

          <div>
            <div className="rounded-2xl bg-white p-8 shadow-lg">
              <div className="text-slate-900 font-medium mb-2">Upload example</div>
              <div className="h-48 bg-champagne rounded-md flex items-center justify-center text-slate-700">
                PDF preview / mockup
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  )
}
