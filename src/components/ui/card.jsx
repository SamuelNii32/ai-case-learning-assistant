import React from 'react'

const Card = React.forwardRef(({ className = '', ...props }, ref) => {
  const classes = `bg-white rounded-lg border border-slate-200 shadow-lg ${className}`

  return <div ref={ref} className={classes} {...props} />
})

Card.displayName = 'Card'

export { Card }
