import React from 'react'

const sections = [
  {
    title: 'What CasePilot Is',
    body: 'An AI-assisted workspace built for case study learning, CasePilot blends the source document with guided analysis tools so every observation stays connected to evidence.',
  },
  {
    title: 'Who It Is For',
    body: 'Students working through complex cases, document-based assignments, or PDF readings rely on CasePilot to keep their notes, insights, and questions organized in one refined place.',
  },
  {
    title: 'Why It Matters',
    body: 'By encouraging deliberate study habits and making it easy to revisit past sessions, CasePilot helps learners sharpen their reasoning and prepare for discussions or assessments with confidence.',
  },
]

export default function About() {
  return (
    <section className="bg-[#fdf9f4] py-16">
      <div className="mx-auto max-w-[900px] space-y-10 px-4 text-[#2c2218]">
        <header className="space-y-3 text-center">
          <p className="text-sm uppercase tracking-[0.3em] text-[#b67e49]">CasePilot</p>
          <h1 className="text-[36px] font-semibold">About CasePilot</h1>
        </header>

        <div className="space-y-6">
          {sections.map(section => (
            <article
              key={section.title}
              className="rounded-[26px] bg-white/90 p-6 shadow-[0_15px_30px_rgba(44,34,24,0.1)]"
            >
              <h2 className="text-[20px] font-semibold text-[#2c2218]">{section.title}</h2>
              <p className="mt-3 text-[16px] leading-[1.6] text-[#5c4c3c]">{section.body}</p>
            </article>
          ))}
        </div>
      </div>
    </section>
  )
}
