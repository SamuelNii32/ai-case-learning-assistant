import React from 'react'

const details = [
  {
    title: 'Student-focused data use',
    body:
      'CasePilot processes case materials, notes, and interaction history solely to power guided analysis and keep every insight traceable to the source document.',
  },
  {
    title: 'Transparency & control',
    body:
      'You decide which files are uploaded, what notes stay private, and when to archive a session; the workspace does not share your documents outside your class unless explicitly configured to do so.',
  },
  {
    title: 'Security posture',
    body:
      'Credentials are hashed, files are stored with layered encryption, and the platform undergoes routine reviews so your study materials remain protected while you learn.',
  },
]

export default function Privacy() {
  return (
    <section className="bg-[#fdfaf4] py-16 px-4">
      <div className="mx-auto max-w-[980px] space-y-10 text-[#2c2218]">
        <header className="space-y-3 text-center">
          <p className="text-sm uppercase tracking-[0.3em] text-[#b67e49]">CasePilot</p>
          <h1 className="text-[36px] font-semibold">Privacy</h1>
          <p className="mx-auto max-w-[720px] text-[16px] leading-[1.6] text-[#5c4c3c]">
            We design CasePilot to be a trusted study companion. This page explains how your case
            materials and interactions are handled so you can focus on learning without doubt.
          </p>
        </header>

        <div className="grid gap-6 md:grid-cols-3">
          {details.map(entry => (
            <article
              key={entry.title}
              className="rounded-[24px] bg-white p-6 shadow-[0_15px_30px_rgba(44,34,24,0.1)]"
            >
              <h2 className="text-[18px] font-semibold text-[#2c2218]">{entry.title}</h2>
              <p className="mt-3 text-[15px] leading-[1.6] text-[#5c4c3c]">{entry.body}</p>
            </article>
          ))}
        </div>

        <div className="rounded-[28px] bg-[#fffdf8] p-8 shadow-[0_20px_35px_rgba(44,34,24,0.12)]">
          <h2 className="text-[20px] font-semibold text-[#2c2218]">Understand how data flows</h2>
          <p className="mt-3 text-[15px] leading-[1.6] text-[#5c4c3c]">
            Your uploaded case notes and highlights stay tied to the class workspace defined by your
            instructor. We keep logs for continuity, but you can delete or export any session when
            you no longer need it. If you have questions, reach out to support@casepilot.com and we will clarify retention, export, or access controls for your account.
          </p>
        </div>
      </div>
    </section>
  )
}
