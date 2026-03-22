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
        <div className="grid grid-cols-1 md:grid-cols-[minmax(0,340px)_minmax(0,760px)_minmax(0,340px)] gap-y-14 md:gap-y-0 gap-x-10 md:gap-x-12 lg:gap-x-16 items-start">
          <div className="space-y-16 md:-mt-4">
            <article className="rounded-[28px] bg-[#f5f4f2] px-6 py-5 text-[#2f2118] shadow-[0_15px_32px_rgba(0,0,0,0.16)] md:max-w-[300px] md:mx-auto">
              <div className="flex items-center gap-3">
                <span className="h-8 w-8 shrink-0 rounded-[14px] bg-[#d2a47f]"></span>

                <h2 className="whitespace-nowrap text-[22px] font-medium leading-[1.2] tracking-[-0.02em] text-[#2b120d]">
                  Tutorial Response
                </h2>
              </div>

              <p className="mt-4 max-w-[420px] text-[13px] leading-[1.4] text-[#a65f17]">
                Stay grounded in the case while AI walks you through each decision with context.
              </p>
            </article>

            <article className="bg-[#8B735E] rounded-[28px] shadow-[0_16px_34px_rgba(15,10,7,0.32)] px-5 py-4 border border-[#6c4c38] text-white max-w-[280px] md:mx-auto">
              <h2 className="text-[22px] font-semibold leading-tight tracking-[-0.01em]">
                Session History &amp; Notes
              </h2>
              <p className="mt-3 text-sm text-neutral-200 leading-snug">
                Review every insight and annotation in one warm, centralized place.
              </p>
            </article>

            <div className="flex justify-end">
              <article className="bg-[#241710] rounded-[28px] shadow-[0_10px_24px_rgba(15,10,7,0.3)] px-5 py-4 border border-[#6c4c38] text-white md:max-w-[220px]">
                <p className="text-sm text-white/80 leading-relaxed">
                  Lorem ipsum dolor sit amet, consectetur adipiscing elit.
                </p>
              </article>
            </div>
          </div>

          <div className="flex justify-center md:relative md:-mt-6">
            <Hero />
          </div>

          <div className="space-y-10 md:pt-8 md:-mt-6">
            <article className="bg-[#C49A77] rounded-[28px] shadow-[0_14px_32px_rgba(44,34,24,0.12)] px-6 py-5 text-[#2c2218] border border-[#f3e3cf] max-w-[340px] md:mx-auto">
              <h2 className="text-[22px] font-semibold leading-snug tracking-[-0.01em]">
                Private by <span className="text-[#2c2218] font-black">DESIGN</span>
              </h2>
              <p className="mt-3 text-sm text-white leading-relaxed">
                Case data stays encrypted and auditable, with privacy baked into every workflow.
              </p>
            </article>

            <article className="bg-white/90 rounded-[34px] shadow-[0_18px_40px_rgba(44,34,24,0.12)] p-8 text-[#2c2218] md:max-w-[420px]">
              <h2 className="text-[22px] font-semibold mt-3 flex items-center justify-center gap-4 text-center">
                <span className="h-3 w-3 rounded-[4px] bg-[#e58a2a]"></span>
                <span>Key Insights Found</span>
                <span className="h-3 w-3 rounded-[4px] bg-[#e58a2a]"></span>
              </h2>
              <ul className="mt-4 space-y-3 text-sm text-[#e58a2a] leading-relaxed list-disc list-inside">
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
