import React from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import RequireAuth from './RequireAuth'
import { AuthContext } from '@/contexts/AuthContext'

function renderRoute(auth) {
  return render(
    <AuthContext.Provider value={auth}>
      <MemoryRouter initialEntries={['/protected']}>
        <Routes>
          <Route path="/login" element={<div>Login page</div>} />
          <Route
            path="/protected"
            element={
              <RequireAuth>
                <div>Protected content</div>
              </RequireAuth>
            }
          />
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>
  )
}

describe('RequireAuth', () => {
  it('waits while a BFF session is being restored', () => {
    renderRoute({ authMode: 'bff', isLoading: true, loggedIn: false, user: null })
    expect(screen.getByText(/checking authentication/i)).toBeInTheDocument()
  })

  it('allows a linked BFF user through', () => {
    renderRoute({
      authMode: 'bff',
      isLoading: false,
      loggedIn: true,
      user: { userId: 'student-1', role: 'student' },
    })
    expect(screen.getByText('Protected content')).toBeInTheDocument()
  })

  it('redirects unauthenticated and unlinked users to login', async () => {
    renderRoute({ authMode: 'bff', isLoading: false, loggedIn: false, user: null })
    await waitFor(() => expect(screen.getByText('Login page')).toBeInTheDocument())
  })
})
