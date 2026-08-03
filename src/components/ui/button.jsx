import React from 'react'

const Button = React.forwardRef(
  ({ className = '', variant = 'default', size = 'default', asChild = false, ...props }, ref) => {
    const baseClasses =
      'inline-flex items-center justify-center rounded-md text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#C96A08]/30 focus-visible:ring-offset-2 disabled:opacity-50 disabled:pointer-events-none'

    const variants = {
      default: 'bg-brand text-white hover:bg-brand-hover',
      outline:
        'border border-warm-border bg-white text-warm-muted hover:border-brand/30 hover:bg-brand-soft hover:text-warm-text',
      ghost: 'hover:bg-brand-soft hover:text-warm-text',
      warm: 'bg-brand text-white hover:bg-brand-hover',
    }

    const sizes = {
      default: 'h-10 px-4 rounded-md',
      sm: 'h-9 px-3 rounded-md',
      lg: 'h-11 px-6 rounded-md',
    }

    const classes = `${baseClasses} ${variants[variant]} ${sizes[size]} ${className}`

    if (asChild) {
      return React.cloneElement(props.children, {
        className: classes,
        ref,
        ...props,
      })
    }

    return <button className={classes} ref={ref} {...props} />
  }
)

Button.displayName = 'Button'

export { Button }
