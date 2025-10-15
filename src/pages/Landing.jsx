import React from 'react'
import { Hero } from '../components/Hero'
import { PreviewMock } from '../components/PreviewMock'
import { ValueProps } from '../components/ValueProps'
import { HowItWorks } from '../components/HowItWorks'

// Landing page visual only. Header/Footer and app chrome are provided by App.jsx
export default function Landing() {
  return (
    <div className="min-h-screen bg-white">
      {/* Hero + Preview side by side */}
      <section className="container mx-auto px-6 py-20">
        <div className="grid lg:grid-cols-2 gap-12 items-center">
          <Hero />
          <PreviewMock />
        </div>
      </section>

      <section id="features" className="container mx-auto px-6 py-20">
        <ValueProps />
      </section>

      <section id="how-it-works" className="container mx-auto px-6 py-20 bg-slate-50">
        <HowItWorks />
      </section>
    </div>
  )
}
