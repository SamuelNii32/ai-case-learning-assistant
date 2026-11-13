import React from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import Classes from './Classes'
import { AuthContext } from '@/contexts/AuthContext'
import { getClasses } from '@/lib/api'

vi.mock('@/lib/api', () => ({
  getClasses: vi.fn(),
  createClass: vi.fn(),
}))

describe('Instructor classes page', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('loads instructor-owned classes and renders the Create Class action', async () => {
    getClasses.mockResolvedValue([])

    render(
      <AuthContext.Provider
        value={{
          authMode: 'bff',
          loggedIn: true,
          user: { userId: 'instructor-a', role: 'instructor' },
        }}
      >
        <MemoryRouter initialEntries={['/admin/classes']}>
          <Classes />
        </MemoryRouter>
      </AuthContext.Provider>
    )

    await waitFor(() => expect(getClasses).toHaveBeenCalledTimes(1))
    expect(screen.getByRole('button', { name: /create class/i })).toBeInTheDocument()
    expect(screen.getByText(/no classes yet/i)).toBeInTheDocument()
  })
})
