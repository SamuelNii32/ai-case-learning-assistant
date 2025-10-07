import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect } from 'vitest'
import Counter from '../components/Counter'

describe('Counter Component', () => {
  it('renders with default initial value', () => {
    render(<Counter />)
    expect(screen.getByTestId('count')).toHaveTextContent('0')
  })

  it('renders with custom initial value', () => {
    render(<Counter initialValue={5} />)
    expect(screen.getByTestId('count')).toHaveTextContent('5')
  })

  it('increments count when increment button is clicked', async () => {
    const user = userEvent.setup()
    render(<Counter />)
    
    const incrementButton = screen.getByTestId('increment')
    const countDisplay = screen.getByTestId('count')
    
    await user.click(incrementButton)
    expect(countDisplay).toHaveTextContent('1')
    
    await user.click(incrementButton)
    expect(countDisplay).toHaveTextContent('2')
  })

  it('decrements count when decrement button is clicked', async () => {
    const user = userEvent.setup()
    render(<Counter initialValue={5} />)
    
    const decrementButton = screen.getByTestId('decrement')
    const countDisplay = screen.getByTestId('count')
    
    await user.click(decrementButton)
    expect(countDisplay).toHaveTextContent('4')
  })

  it('resets count to zero when reset button is clicked', async () => {
    const user = userEvent.setup()
    render(<Counter initialValue={10} />)
    
    const resetButton = screen.getByTestId('reset')
    const countDisplay = screen.getByTestId('count')
    
    await user.click(resetButton)
    expect(countDisplay).toHaveTextContent('0')
  })

  it('handles multiple operations correctly', async () => {
    const user = userEvent.setup()
    render(<Counter />)
    
    const incrementButton = screen.getByTestId('increment')
    const decrementButton = screen.getByTestId('decrement')
    const countDisplay = screen.getByTestId('count')
    
    // Increment 3 times
    await user.click(incrementButton)
    await user.click(incrementButton)
    await user.click(incrementButton)
    expect(countDisplay).toHaveTextContent('3')
    
    // Decrement once
    await user.click(decrementButton)
    expect(countDisplay).toHaveTextContent('2')
  })

  it('applies correct CSS classes', () => {
    render(<Counter />)
    
    const container = screen.getByText('Counter Component').closest('div')
    expect(container).toHaveClass('p-4', 'bg-white', 'rounded-lg', 'shadow')
    
    const incrementButton = screen.getByTestId('increment')
    expect(incrementButton).toHaveClass('bg-blue-500', 'hover:bg-blue-600', 'text-white')
  })
})