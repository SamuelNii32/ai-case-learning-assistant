import React, { useContext } from 'react'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'

const jsonResponse = (body, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })

async function renderAuth({ bff, responses, navigateTopLevel = vi.fn() }) {
  vi.resetModules()
  vi.stubEnv('VITE_AUTH0_BFF_ENABLED', bff ? 'true' : 'false')
  const fetchMock = vi.fn()
  responses.forEach(response => fetchMock.mockResolvedValueOnce(response))
  vi.stubGlobal('fetch', fetchMock)

  const { AuthContext, default: AuthProvider } = await import('./AuthContext')
  function Probe() {
    const auth = useContext(AuthContext)
    return (
      <div>
        <output data-testid="mode">{auth.authMode}</output>
        <output data-testid="status">{auth.authStatus}</output>
        <output data-testid="user">{auth.user?.userId || 'none'}</output>
        <output data-testid="logged-in">{String(auth.loggedIn)}</output>
        <button type="button" onClick={() => void auth.logout()}>
          Log out
        </button>
      </div>
    )
  }

  render(
    <AuthProvider navigateTopLevel={navigateTopLevel}>
      <Probe />
    </AuthProvider>
  )
  return { fetchMock, navigateTopLevel }
}

describe('AuthProvider authentication modes', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.restoreAllMocks()
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    vi.unstubAllGlobals()
  })

  it('restores a linked BFF session and loads the live local user', async () => {
    const { fetchMock } = await renderAuth({
      bff: true,
      responses: [
        jsonResponse({ authenticated: true, accountLinked: true, status: 'linked' }),
        jsonResponse({ userId: 'local-student', role: 'student', email: 'student@test.invalid' }),
      ],
    })

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))
    expect(screen.getByTestId('user')).toHaveTextContent('local-student')
    expect(screen.getByTestId('logged-in')).toHaveTextContent('true')
    expect(fetchMock).toHaveBeenNthCalledWith(1, '/bff/session', {
      credentials: 'same-origin',
    })
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/me', { credentials: 'same-origin' })
    expect(localStorage.getItem('authToken')).toBeNull()
    expect(localStorage.getItem('authUser')).toBeNull()
  })

  it('reports an unlinked Auth0 account without requesting /me', async () => {
    const { fetchMock } = await renderAuth({
      bff: true,
      responses: [
        jsonResponse({
          authenticated: true,
          accountLinked: false,
          status: 'account_not_linked',
        }),
      ],
    })

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('unlinked'))
    expect(screen.getByTestId('logged-in')).toHaveTextContent('false')
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('treats a missing or expired BFF session as unauthenticated', async () => {
    await renderAuth({
      bff: true,
      responses: [
        jsonResponse({ authenticated: false, accountLinked: false, status: 'not_authenticated' }),
      ],
    })

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('unauthenticated'))
    expect(screen.getByTestId('logged-in')).toHaveTextContent('false')
  })

  it('finishes initialization and shows Auth0 login for an unauthenticated BFF session', async () => {
    vi.resetModules()
    vi.stubEnv('VITE_AUTH0_BFF_ENABLED', 'true')
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ authenticated: false, accountLinked: false, status: 'not_authenticated' })
    )
    vi.stubGlobal('fetch', fetchMock)

    const { default: AuthProvider } = await import('./AuthContext')
    const { default: SignInPage } = await import('../pages/SignIn')

    render(
      <AuthProvider>
        <MemoryRouter initialEntries={['/login']}>
          <SignInPage />
        </MemoryRouter>
      </AuthProvider>
    )

    expect(await screen.findByRole('button', { name: /continue with auth0/i })).toBeEnabled()
    expect(screen.queryByText(/checking session/i)).not.toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledTimes(1)

    await act(async () => {
      await Promise.resolve()
    })
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('logs out through the CSRF-protected BFF endpoint', async () => {
    const navigateTopLevel = vi.fn()
    const { fetchMock } = await renderAuth({
      bff: true,
      navigateTopLevel,
      responses: [
        jsonResponse({ authenticated: true, accountLinked: true, status: 'linked' }),
        jsonResponse({ userId: 'local-student', role: 'student' }),
        jsonResponse({ requestToken: 'logout-csrf' }),
        jsonResponse({ logoutUrl: '/bff/logout/complete?ticket=one-time-ticket' }),
      ],
    })
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))

    await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Log out' })))

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(4))
    expect(fetchMock).toHaveBeenNthCalledWith(
      4,
      '/bff/logout/prepare?returnUrl=/login',
      expect.objectContaining({
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'X-CSRF-TOKEN': 'logout-csrf' },
      })
    )
    expect(navigateTopLevel).toHaveBeenCalledWith(
      '/bff/logout/complete?ticket=one-time-ticket'
    )
  })

  it('preserves the existing JWT startup flow', async () => {
    localStorage.setItem('authToken', 'legacy.header.signature')
    const { fetchMock } = await renderAuth({
      bff: false,
      responses: [jsonResponse({ userId: 'legacy-user', role: 'instructor' })],
    })

    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('legacy-user'))
    expect(screen.getByTestId('mode')).toHaveTextContent('jwt')
    expect(fetchMock.mock.calls[0][1].headers.Authorization).toBe('Bearer legacy.header.signature')
    expect(localStorage.getItem('authToken')).toBe('legacy.header.signature')
  })
})
