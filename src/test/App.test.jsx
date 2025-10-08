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
    expect(screen.getByText('Welcome to AI Case Learning Assistant')).toBeInTheDocument()
  })

  it('displays the features list', () => {
    renderApp()
    expect(screen.getByText('✅ AI-powered case analysis')).toBeInTheDocument()
    expect(screen.getByText('✅ Interactive learning modules')).toBeInTheDocument()
    expect(screen.getByText('✅ Progress tracking')).toBeInTheDocument()
    expect(screen.getByText('✅ Comprehensive case library')).toBeInTheDocument()
  })

  it('renders the header component', () => {
    renderApp()
    expect(screen.getByText('AI Case Assistant')).toBeInTheDocument()
  })

  it('has navigation links', () => {
    renderApp()
    expect(screen.getByText('About')).toBeInTheDocument()
    expect(screen.getByText('Privacy')).toBeInTheDocument()
    expect(screen.getByText('Contact')).toBeInTheDocument()
    expect(screen.getByText('Sign In')).toBeInTheDocument()
  })
})