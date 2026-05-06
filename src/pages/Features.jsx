import React from 'react'
import { BookOpen, ClipboardList, LayoutDashboard, Clock3, Search } from 'lucide-react'

const features = [
  {
    title: 'Guided Case Analysis',
    description:
      'Step through each case with guided prompts that surface context, constraints, and decision points so you develop a structured argument for every recommendation.',
    icon: LayoutDashboard,
  },
  {
    title: 'Grounded Answers',
    description:
      'Responses link directly to the case material, enabling you to trace every insight back to supporting evidence as you study.',
    icon: Search,
  },
  {
    title: 'Notes and Highlights',
    description:
      'Capture key ideas, mark important passages, and return to those moments later without losing the original logic of the document.',
    icon: ClipboardList,
  },
  {
    title: 'Session History',
    description:
      'Review what you worked on, where you paused, and which questions you explored to keep learning continuous across study sessions.',
    icon: Clock3,
  },
  {
    title: 'Focused Learning Workspace',
    description:
      'Case content, AI reflections, and your annotations stay together in a calm, organized layout that keeps the focus on understanding.',
    icon: BookOpen,
  },
]

export default function Features() {
  return (
    <section className="bg-[#fdfaf4] py-16">
      <div className="mx-auto max-w-[1100px] space-y-10 px-4">
        <header className="space-y-4 text-center">
          <h1 className="text-[38px] font-semibold tracking-tight text-[#2c2218]">Features</h1>
          <p className="mx-auto max-w-[760px] text-[17px] leading-7 text-[#5c4c3c]">
            CasePilot helps students engage with case materials in a deliberate, evidence-based way,
            combining guided analysis with annotated sources to keep every conclusion anchored in
            the work itself.
          </p>
        </header>

        <div className="grid gap-6 md:grid-cols-2 lg:grid-cols-3">
          {features.map(feature => (
            <article
              key={feature.title}
              className="flex h-full flex-col rounded-[28px] bg-white p-6 shadow-[0_15px_30px_rgba(44,34,24,0.12)]"
            >
              <div className="flex h-12 w-12 items-center justify-center rounded-full bg-[#f6ecde] text-[#b35e07]">
                <feature.icon className="h-5 w-5" />
              </div>
              <h2 className="mt-6 text-[18px] font-semibold text-[#2c2218]">{feature.title}</h2>
              <p className="mt-3 flex-1 text-[15px] leading-[1.6] text-[#5c4c3c]">
                {feature.description}
              </p>
            </article>
          ))}
        </div>
      </div>
    </section>
  )
}
