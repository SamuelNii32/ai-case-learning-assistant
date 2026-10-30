import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const jsonResponse = (body, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })

describe('Upload page in BFF mode', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.stubEnv('VITE_AUTH_MODE', 'bff')
    vi.stubEnv('VITE_AUTH0_BFF_ENABLED', 'true')
    vi.stubEnv('VITE_API_BASE', '')
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllEnvs()
    vi.unstubAllGlobals()
  })

  it('uploads through the same-origin client when VITE_API_BASE is empty', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ requestToken: 'upload-csrf' }))
      .mockResolvedValueOnce(jsonResponse({ uploadId: 'upload-1' }))
      .mockResolvedValueOnce(jsonResponse({ pages: 1, fileSizeBytes: 4 }))
    vi.stubGlobal('fetch', fetchMock)
    const alertMock = vi.spyOn(window, 'alert').mockImplementation(() => {})

    const { AuthContext } = await import('@/contexts/AuthContext')
    const { default: UploadPage } = await import('./Upload')

    render(
      <AuthContext.Provider value={{ authMode: 'bff', user: { role: 'student' } }}>
        <MemoryRouter>
          <UploadPage />
        </MemoryRouter>
      </AuthContext.Provider>
    )

    const file = new File(['test'], 'case.pdf', { type: 'application/pdf' })
    fireEvent.change(screen.getByLabelText(/upload pdf file/i), {
      target: { files: [file] },
    })

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3))
    expect(alertMock).not.toHaveBeenCalled()
    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      '/bff/csrf',
      expect.objectContaining({ method: 'GET', credentials: 'same-origin' })
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/uploads',
      expect.objectContaining({
        method: 'POST',
        credentials: 'same-origin',
        body: expect.any(FormData),
        headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'upload-csrf' }),
      })
    )
    expect(fetchMock.mock.calls[1][1].headers.Authorization).toBeUndefined()
  })
})
