import React from 'react'

export default function Support() {
  return (
    <section className="bg-[#fdfbf6] py-16">
      <div className="mx-auto max-w-[900px] space-y-8 px-4 text-[#2c2218]">
        <header className="space-y-3 text-center">
          <p className="text-sm uppercase tracking-[0.3em] text-[#b67e49]">Support</p>
          <h1 className="text-[36px] font-semibold">Support</h1>
          <p className="mx-auto max-w-[700px] text-[16px] leading-[1.6] text-[#5c4c3c]">
            Reach out if you need clarification on how CasePilot structures a session, connect your
            materials, or keep your workspace aligned with the class workflow.
          </p>
        </header>

        <div className="rounded-[28px] bg-white p-8 text-center shadow-[0_20px_35px_rgba(44,34,24,0.1)]">
          <p className="text-[16px] leading-[1.6] text-[#5c4c3c]">
            Email us at{' '}
            <a href="mailto:support@casepilot.com" className="font-semibold text-[#b35e07]">
              support@casepilot.com
            </a>{' '}
            and we will respond as soon as possible with the next practical step.
          </p>
          <a
            href="mailto:support@casepilot.com"
            className="mt-6 inline-flex items-center justify-center rounded-full bg-[#b35e07] px-6 py-3 text-sm font-semibold uppercase tracking-[0.3em] text-white shadow-lg"
          >
            Contact Support
          </a>
        </div>

        <div className="rounded-[26px] bg-[#fffdf8] p-6 shadow-[0_12px_25px_rgba(44,34,24,0.08)]">
          <h2 className="text-[18px] font-semibold text-[#2c2218]">Account or access questions</h2>
          <p className="mt-3 text-[15px] leading-[1.6] text-[#5c4c3c]">
            If you need help signing in or syncing your classroom access, mention your course or
            instructor to help us address your environment quickly.
          </p>
        </div>
      </div>
    </section>
  )
}
