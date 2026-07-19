import React from 'react'

const Button = React.forwardRef(
  ({ className = '', variant = 'default', size = 'default', asChild = false, ...props }, ref) => {
    const baseClasses =
      'inline-flex items-center justify-center rounded-md text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#C96A08]/30 focus-visible:ring-offset-2 disabled:opacity-50 disabled:pointer-events-none'

    const variants = {
      default: 'bg-[#125691] text-white hover:bg-[#0f4f74]',
      outline:
        'border border-[#d9c4ad] bg-white text-[#5C4C3C] hover:border-[#C96A08]/30 hover:bg-[#fff6eb] hover:text-[#2c2218]',
      ghost: 'hover:bg-[#fff6eb] hover:text-[#2c2218]',
      warm: 'bg-[#C96A08] text-white hover:bg-[#b85f0a]',
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
