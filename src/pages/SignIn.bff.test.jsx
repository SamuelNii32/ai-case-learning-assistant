import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import SignInPage from './SignIn'
import { AuthContext } from '@/contexts/AuthContext'

describe('SignIn BFF mode', () => {
  it('starts the server-side Auth0 login instead of showing the password form', () => {
    const loginWithAuth0 = vi.fn()
    render(
      <AuthContext.Provider
        value={{
          authMode: 'bff',
          authStatus: 'unauthenticated',
          isLoading: false,
          loggedIn: false,
          user: null,
          loginWithAuth0,
          logout: vi.fn(),
        }}
      >
        <MemoryRouter initialEntries={['/login']}>
          <SignInPage />
        </MemoryRouter>
      </AuthContext.Provider>
    )

    expect(screen.queryByLabelText(/email/i)).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /continue with auth0/i }))
    expect(loginWithAuth0).toHaveBeenCalledWith('/login')
  })

  it('shows a clear account-not-linked state', () => {
    render(
      <AuthContext.Provider
        value={{
          authMode: 'bff',
          authStatus: 'unlinked',
          isLoading: false,
          loggedIn: false,
          user: null,
          loginWithAuth0: vi.fn(),
          logout: vi.fn(),
        }}
      >
        <MemoryRouter initialEntries={['/login']}>
          <SignInPage />
        </MemoryRouter>
      </AuthContext.Provider>
    )

    expect(screen.getByText(/not linked to a CasePilot account/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /retry auth0 sign-in/i })).toBeInTheDocument()
  })
})
