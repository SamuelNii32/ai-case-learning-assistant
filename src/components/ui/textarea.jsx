import React from 'react'

export const Textarea = React.forwardRef(({ className = '', ...props }, ref) => {
  const base = 'w-full rounded-md border px-3 py-2 text-sm resize-vertical'
  return <textarea ref={ref} className={`${base} ${className}`} {...props} />
})

Textarea.displayName = 'Textarea'
