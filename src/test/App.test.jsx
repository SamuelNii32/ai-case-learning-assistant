import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import App from '../App'

// App already includes Router, so render directly
const renderApp = () => {
  return render(<App />)
}

describe('App Component', () => {
  it('renders the main heading', () => {
    renderApp()
    expect(screen.getByText('✅ React + Tailwind + Router Working!')).toBeInTheDocument()
  })

  it('displays the technology checklist', () => {
    renderApp()
    expect(screen.getByText('✅ React (working)')).toBeInTheDocument()
    expect(screen.getByText('✅ Vite (working)')).toBeInTheDocument()
    expect(screen.getByText('✅ Tailwind CSS (working)')).toBeInTheDocument()
    expect(screen.getByText('✅ React Router (installed & working)')).toBeInTheDocument()
  })

  it('shows the home route message', () => {
    renderApp()
    expect(screen.getByText('🎉 You\'re on the home route!')).toBeInTheDocument()
  })

  it('has the correct CSS classes applied', () => {
    renderApp()
    const mainDiv = screen.getByText('✅ React + Tailwind + Router Working!').closest('div')
    expect(mainDiv).toHaveClass('bg-blue-500', 'text-white', 'p-8', 'min-h-screen')
  })
})