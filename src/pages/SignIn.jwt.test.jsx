import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import SignInPage from './SignIn'
import { AuthContext } from '@/contexts/AuthContext'

const loginResponse = body =>
  new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })

describe('SignIn JWT role handling', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('does not grant instructor UI state from the client role selector', async () => {
    const login = vi.fn()
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        loginResponse({
          token: 'legacy.header.signature',
          userId: 'local-student',
          email: 'student@example.test',
          fullName: 'Local Student',
          isSuperUser: false,
        })
      )
    )

    render(
      <AuthContext.Provider
        value={{
          authMode: 'jwt',
          loggedIn: false,
          user: null,
          login,
        }}
      >
        <MemoryRouter initialEntries={['/login']}>
          <Routes>
            <Route path="/login" element={<SignInPage />} />
            <Route path="/dashboard" element={<div>Student dashboard</div>} />
            <Route path="/admin/classes" element={<div>Instructor classes</div>} />
          </Routes>
        </MemoryRouter>
      </AuthContext.Provider>
    )

    fireEvent.change(screen.getByLabelText(/email/i), {
      target: { value: 'student@example.test' },
    })
    fireEvent.change(screen.getByLabelText(/^password$/i), {
      target: { value: 'correct-password' },
    })
    fireEvent.click(screen.getByLabelText(/instructor/i))
    fireEvent.click(screen.getByRole('button', { name: /^sign in$/i }))

    await waitFor(() =>
      expect(login).toHaveBeenCalledWith(
        'legacy.header.signature',
        expect.objectContaining({ userId: 'local-student', role: 'student' })
      )
    )
    expect(await screen.findByText('Student dashboard')).toBeInTheDocument()
    expect(screen.queryByText('Instructor classes')).not.toBeInTheDocument()
  })
})
