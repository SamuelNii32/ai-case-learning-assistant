import React from 'react'

const Input = React.forwardRef(({ className = '', type = 'text', ...props }, ref) => {
  const classes = `flex h-10 w-full rounded-md border border-[#dbc6ae] bg-white/90 px-3 py-2 text-sm ring-offset-white file:border-0 file:bg-transparent file:text-sm file:font-medium placeholder:text-[#9c7a65] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#C96A08] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 ${className}`

  return <input type={type} className={classes} ref={ref} {...props} />
})

Input.displayName = 'Input'

export { Input }
