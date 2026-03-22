import React from 'react'
import { Link } from 'react-router-dom'

const columnLinks = [
  ['Product', [{ label: 'Features', to: '/features' }]],
  ['Company', [{ label: 'About', to: '/about' }]],
  ['Resources', [{ label: 'Support', to: '/support' }]],
]

export function Footer() {
  return (
    <footer>
      <section className="bg-[#8B7462] text-[#F5EDE2]">
        <div className="container mx-auto px-6 py-12">
          <div className="grid gap-10 md:grid-cols-[minmax(0,1fr)_minmax(0,420px)]">
            <div className="max-w-[280px] space-y-4">
              <h3 className="text-xl font-semibold">CasePilot</h3>
              <div className="h-px w-16 bg-[#F5EDE2]/60" />
              <p className="text-sm leading-relaxed text-[#F0E8DA]">
                A guided learning companion that keeps every case, note, and insight within reach
                for students and instructors.
              </p>
            </div>

            <div className="grid grid-cols-1 gap-6 md:grid-cols-3 md:justify-end">
              {columnLinks.map(([title, links]) => (
                <div key={title}>
                  <h4 className="mb-4 text-lg font-semibold">{title}</h4>
                  <ul className="space-y-2 text-sm">
                    {links.map(link => (
                      <li key={link.label}>
                        <Link className="text-[#F0E8DA] hover:text-white" to={link.to}>
                          {link.label}
                        </Link>
                      </li>
                    ))}
                  </ul>
                </div>
              ))}
            </div>
          </div>
        </div>
      </section>

      <section className="bg-[#E9E1D6] text-[#5C4C3C]">
        <div className="container mx-auto flex flex-col gap-4 px-6 py-6 md:flex-row md:items-center md:justify-between">
          <p className="text-sm">All Rights Reserved</p>
          <div className="flex gap-6 text-sm">
            <Link to="/terms" className="hover:text-[#8B7462]">
              Terms
            </Link>
            <Link to="/privacy" className="hover:text-[#8B7462]">
              Privacy
            </Link>
            <Link to="/cookies" className="hover:text-[#8B7462]">
              Cookies
            </Link>
          </div>
        </div>
      </section>
    </footer>
  )
}
