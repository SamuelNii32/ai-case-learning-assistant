import React from 'react'

export default function Contact() {
  return (
    <section className="bg-[#fefcf7] py-16 px-4">
      <div className="mx-auto max-w-[980px] space-y-10 text-[#2c2218]">
        <header className="space-y-3 text-center">
          <p className="text-sm uppercase tracking-[0.3em] text-[#b67e49]">Connect</p>
          <h1 className="text-[36px] font-semibold">Contact</h1>
          <p className="mx-auto max-w-[720px] text-[16px] leading-[1.6] text-[#5c4c3c]">
            CasePilot support is staffed to respond to student or faculty questions about getting
            started, uploading materials, or aligning the platform with your syllabus requirements.
          </p>
        </header>

        <div className="rounded-[28px] bg-white p-8 shadow-[0_20px_35px_rgba(44,34,24,0.12)]">
          <h2 className="text-[20px] font-semibold text-[#2c2218]">Contact support</h2>
          <p className="mt-3 text-[15px] leading-[1.6] text-[#5c4c3c]">
            Email us at{' '}
            <a href="mailto:support@casepilot.com" className="font-semibold text-[#b35e07]">
              support@casepilot.com
            </a>{' '}
            with a brief description of your course, the case or PDF you are working with, and the
            type of guidance you need.
          </p>
          <div className="mt-6 text-center">
            <a
              href="mailto:support@casepilot.com"
              className="inline-flex items-center justify-center rounded-full bg-[#b35e07] px-7 py-3 text-sm font-semibold uppercase tracking-[0.3em] text-white shadow-lg transition-colors hover:bg-[#9c4d07]"
            >
              Contact Support
            </a>
          </div>
        </div>

        <div className="grid gap-6 md:grid-cols-2">
          <article className="rounded-[24px] bg-[#fffdf8] p-6 shadow-[0_12px_25px_rgba(44,34,24,0.08)]">
            <h3 className="text-[18px] font-semibold text-[#2c2218]">Account or access</h3>
            <p className="mt-3 text-[15px] leading-[1.6] text-[#5c4c3c]">
              If you are unable to sign in or your classroom role does not appear, mention your
              course and instructor so we can trace the proper workspace and permissions quickly.
            </p>
          </article>
          <article className="rounded-[24px] bg-[#fffdf8] p-6 shadow-[0_12px_25px_rgba(44,34,24,0.08)]">
            <h3 className="text-[18px] font-semibold text-[#2c2218]">General feedback</h3>
            <p className="mt-3 text-[15px] leading-[1.6] text-[#5c4c3c]">
              Share ways CasePilot can better support your learning journey or instructor experience,
              and we will review each note to inform future refinements.
            </p>
          </article>
        </div>
      </div>
    </section>
  )
}
