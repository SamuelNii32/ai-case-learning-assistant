import { NavLink } from 'react-router-dom'
import { BookOpen, Upload } from 'lucide-react'

export default function InstructorNav() {
  const navItems = [
    { to: '/admin/classes', label: 'My Classes', icon: BookOpen },
    { to: '/admin/upload', label: 'Upload Cases', icon: Upload },
  ]

  return (
    <nav className="border-b border-border">
      <div className="container mx-auto px-4">
        <div className="flex items-center gap-1">
          {navItems.map(({ to, label, icon: Icon }) => (
            <NavLink
              key={to}
              to={to}
              className={({ isActive }) =>
                `flex items-center gap-2 px-4 py-3 text-sm font-medium transition-colors border-b-2 ${
                  isActive
                    ? 'border-primary text-foreground'
                    : 'border-transparent text-muted-foreground hover:text-foreground hover:border-border'
                }`
              }
            >
              <Icon className="w-4 h-4" />
              {label}
            </NavLink>
          ))}
        </div>
      </div>
    </nav>
  )
}
