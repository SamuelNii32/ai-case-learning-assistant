import { NavLink } from 'react-router-dom'
import { BookOpen, Upload } from 'lucide-react'

export default function InstructorNav() {
  const navItems = [
    { to: '/admin/classes', label: 'My Classes', icon: BookOpen },
    { to: '/admin/upload', label: 'Upload Cases', icon: Upload },
  ]

  return (
    <nav className="border-b border-[#e4d6c7] bg-white/90 backdrop-blur-sm">
      <div className="container mx-auto px-4">
        <div className="flex items-center gap-1 overflow-x-auto whitespace-nowrap">
          {navItems.map(({ to, label, icon: Icon }) => (
            <NavLink
              key={to}
              to={to}
              className={({ isActive }) =>
                `flex items-center gap-2 px-4 py-3 text-sm font-semibold transition-colors border-b-2 ${
                  isActive
                    ? 'border-[#C96A08] text-[#2c2218] bg-[#fff6eb]'
                    : 'border-transparent text-[#7a5c3c] hover:text-[#2c2218] hover:border-[#d9c4ad]'
                }`
              }
            >
              <Icon className="w-4 h-4 shrink-0" />
              {label}
            </NavLink>
          ))}
        </div>
      </div>
    </nav>
  )
}
