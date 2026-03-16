// Landing page was added earlier; placeholder to avoid runtime errors if referenced.
import React, { useContext } from 'react'
import { Hero } from '@/components/Hero'
import { HowItWorks } from '@/components/HowItWorks'
import WorkspacePreview from '@/components/WorkspacePreview'
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
    <main className="bg-[#f8f5ef]">
      <div className="container mx-auto px-4 sm:px-8 lg:px-12 py-20">
        <div className="grid grid-cols-1 md:grid-cols-[minmax(0,260px)_minmax(0,660px)_minmax(0,260px)] gap-y-12 md:gap-y-0 gap-x-12 md:gap-x-18 lg:gap-x-24 items-start">
          <div className="space-y-10 md:-mt-6">
            <article className="bg-white/80 rounded-2xl shadow-[0_16px_35px_rgba(44,34,24,0.07)] border border-[#f5ebe0] text-[#2c2218] p-7">
              <p className="text-xs font-medium uppercase tracking-widest text-[#b7712f]">Guided</p>
              <h2 className="text-2xl font-semibold mt-2">Tutorial Response</h2>
              <p className="mt-3 text-sm text-[#5c4c3c] leading-relaxed">
                Stay grounded in the case while AI walks you through each decision with context.
              </p>
            </article>

            <article className="bg-[#261b15] rounded-[26px] shadow-[0_12px_30px_rgba(15,10,7,0.25)] p-6 text-white border border-[#3c2d23]">
              <p className="text-xs font-semibold tracking-widest text-[#fbbf59]">Recall</p>
              <h2 className="text-2xl font-semibold uppercase">Session History &amp; NOTES</h2>
              <p className="mt-3 text-sm text-neutral-200 leading-relaxed">
                Review every insight and annotation in one warm, centralized place.
              </p>
            </article>
          </div>

          <div className="flex justify-center md:relative md:-mt-6">
            <Hero />
          </div>

          <div className="space-y-8 md:pt-12">
            <article className="bg-[#f6eddc] rounded-[26px] shadow-[0_18px_35px_rgba(44,34,24,0.06)] p-6 text-[#2c2218] border border-[#f3e3cf]">
              <p className="text-xs font-medium uppercase tracking-widest text-[#8f6a3c]">Secure</p>
              <h2 className="text-2xl font-semibold">
                Private by <span className="text-[#C96A08]">DESIGN</span>
              </h2>
              <p className="mt-3 text-sm text-[#5c4c3c] leading-relaxed">
                Case data stays encrypted and auditable, with privacy baked into every workflow.
              </p>
            </article>

            <article className="bg-white/85 rounded-[26px] shadow-[0_16px_32px_rgba(44,34,24,0.07)] p-6 text-[#2c2218]">
              <p className="text-xs font-semibold uppercase tracking-widest text-[#b7712f]">
                Signals
              </p>
              <h2 className="text-2xl font-semibold">Key Insights Found</h2>
              <ul className="mt-3 space-y-2 text-sm text-[#5c4c3c] leading-relaxed list-disc list-inside">
                <li>Evidence-backed conclusions organized by priority.</li>
                <li>Links that jump straight to the supporting documents.</li>
                <li>Shareable summaries for every stakeholder.</li>
              </ul>
            </article>
          </div>
        </div>
      </div>

      <section className="py-16">
        <WorkspacePreview />
      </section>

      <HowItWorks />
    </main>
  )
}
