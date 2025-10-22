import React from 'react'

export default function Badge({ children, className = '', variant = 'default', ...props }) {
  const base = 'inline-flex items-center rounded-full px-2 py-1 text-sm'
  const variants = {
    default: 'bg-blue-600 text-white',
    secondary: 'bg-slate-100 text-slate-700',
  }
  return (
    <span className={`${base} ${variants[variant] || ''} ${className}`} {...props}>
      {children}
    </span>
  )
}
