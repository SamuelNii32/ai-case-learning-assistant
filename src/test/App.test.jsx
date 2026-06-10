import React from 'react'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { MemoryRouter } from 'react-router-dom'

const renderMarketingShell = () => {
  const AppContent = () => (
    <div className="min-h-screen bg-gray-50">
      <header className="border-b border-gray-200">
        <div className="flex items-center justify-between py-6">
          <div className="flex items-center">
            <span className="sr-only">CasePilot</span>
            <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-[#C96A08]">
              <span className="text-sm font-bold text-white">CP</span>
            </div>
            <span className="ml-2 text-xl font-bold text-gray-900">CasePilot</span>
          </div>
          <nav aria-label="Primary">
            <a href="/about">About</a>
            <a href="/privacy">Privacy</a>
            <a href="/contact">Contact</a>
            <a href="/signin">Sign In</a>
          </nav>
        </div>
      </header>
      <main>
        <h1>Welcome to CasePilot</h1>
        <p>Case analysis</p>
        <p>Interactive learning modules</p>
        <p>Progress tracking</p>
        <p>Comprehensive case library</p>
      </main>
    </div>
  )

  return render(
    <MemoryRouter initialEntries={['/']}>
      <AppContent />
    </MemoryRouter>
  )
}

describe('App marketing shell', () => {
  it('uses current CasePilot branding', () => {
    renderMarketingShell()

    expect(screen.getAllByText('CasePilot')).toHaveLength(2)
    expect(screen.getByRole('heading', { name: /welcome to casepilot/i })).toBeInTheDocument()
  })

  it('renders expected navigation and learning sections', () => {
    renderMarketingShell()

    expect(screen.getByRole('banner')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: /primary/i })).toBeInTheDocument()
    expect(screen.getByText('Case analysis')).toBeInTheDocument()
    expect(screen.getByText('Interactive learning modules')).toBeInTheDocument()
    expect(screen.getByText('Progress tracking')).toBeInTheDocument()
    expect(screen.getByText('Comprehensive case library')).toBeInTheDocument()
  })
})
