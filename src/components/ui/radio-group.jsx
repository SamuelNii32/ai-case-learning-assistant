import React from 'react'

const RadioGroup = React.forwardRef(
  ({ className = '', value, onValueChange, children, ...props }, ref) => {
    return (
      <div ref={ref} className={className} role="radiogroup" {...props}>
        {React.Children.map(children, child => {
          if (React.isValidElement(child)) {
            return React.cloneElement(child, {
              checked: child.props.value === value,
              onChange: () => onValueChange?.(child.props.value),
            })
          }
          return child
        })}
      </div>
    )
  }
)

const RadioGroupItem = React.forwardRef(
  ({ className = '', value, checked, onChange, ...props }, ref) => {
    const classes = `aspect-square h-4 w-4 rounded-full border border-slate-300 text-blue-600 ring-offset-white focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 ${className}`

    return (
      <input
        ref={ref}
        type="radio"
        value={value}
        checked={checked}
        onChange={onChange}
        className={classes}
        {...props}
      />
    )
  }
)

RadioGroup.displayName = 'RadioGroup'
RadioGroupItem.displayName = 'RadioGroupItem'

export { RadioGroup, RadioGroupItem }
