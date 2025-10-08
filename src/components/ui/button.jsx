import React from 'react'

const Button = React.forwardRef(({ className = '', variant = 'default', size = 'default', asChild = false, ...props }, ref) => {
  const baseClasses = 'inline-flex items-center justify-center rounded-md text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:opacity-50 disabled:pointer-events-none'
  
  const variants = {
    default: 'bg-blue-600 text-white hover:bg-blue-700',
    outline: 'border border-slate-300 bg-white text-slate-900 hover:bg-slate-50',
    ghost: 'hover:bg-slate-100',
  }
  
  const sizes = {
    default: 'h-10 py-2 px-4',
    sm: 'h-9 px-3 rounded-md',
    lg: 'h-11 px-8 rounded-md',
  }
  
  const classes = `${baseClasses} ${variants[variant]} ${sizes[size]} ${className}`
  
  if (asChild) {
    return React.cloneElement(props.children, {
      className: classes,
      ref,
      ...props
    })
  }
  
  return (
    <button
      className={classes}
      ref={ref}
      {...props}
    />
  )
})

Button.displayName = 'Button'

export { Button }