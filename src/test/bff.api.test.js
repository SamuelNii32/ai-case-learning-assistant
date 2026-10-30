import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const jsonResponse = (body, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })

async function importApi({ bff }) {
  vi.resetModules()
  vi.stubEnv('VITE_AUTH_MODE', bff ? 'bff' : 'jwt')
  vi.stubEnv('VITE_AUTH0_BFF_ENABLED', bff ? 'true' : 'false')
  return import('@/lib/api')
}

describe('BFF-aware API client', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.restoreAllMocks()
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    vi.unstubAllGlobals()
  })

  it('uses a same-origin cookie request without a Bearer token for enabled reads', async () => {
    localStorage.setItem('authToken', 'legacy.header.signature')
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([{ id: 'upload-1' }]))
    vi.stubGlobal('fetch', fetchMock)
    const { listCases } = await importApi({ bff: true })

    await expect(listCases()).resolves.toEqual([{ id: 'upload-1' }])
    expect(fetchMock).toHaveBeenCalledWith(
      '/uploads/mine',
      expect.objectContaining({ method: 'GET', credentials: 'same-origin' })
    )
    expect(fetchMock.mock.calls[0][1].headers.Authorization).toBeUndefined()
  })

  it('adds CSRF protection to every BFF mutation, including uploads', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ requestToken: 'upload-csrf' }))
      .mockResolvedValueOnce(jsonResponse({ uploadId: 'upload-1' }))
    vi.stubGlobal('fetch', fetchMock)
    const { uploadFile } = await importApi({ bff: true })

    await expect(uploadFile(new FormData())).resolves.toEqual({ uploadId: 'upload-1' })
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/uploads',
      expect.objectContaining({
        method: 'POST',
        credentials: 'same-origin',
        headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'upload-csrf' }),
      })
    )
    expect(fetchMock.mock.calls[1][1].headers.Authorization).toBeUndefined()
  })

  it('acquires a CSRF token and sends it on the enabled upload rename', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ requestToken: 'csrf-test-token' }))
      .mockResolvedValueOnce(jsonResponse({ id: 'upload-1', name: 'Renamed' }))
    vi.stubGlobal('fetch', fetchMock)
    const { renameCase } = await importApi({ bff: true })

    await renameCase('upload-1', 'Renamed')

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      '/bff/csrf',
      expect.objectContaining({ method: 'GET', credentials: 'same-origin' })
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/uploads/upload-1/name',
      expect.objectContaining({
        method: 'PATCH',
        credentials: 'same-origin',
        headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-test-token' }),
      })
    )
    expect(fetchMock.mock.calls[1][1].headers.Authorization).toBeUndefined()
  })

  it('protects the legacy join-code GET because it may create a code', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ requestToken: 'join-code-csrf' }))
      .mockResolvedValueOnce(jsonResponse({ classId: 'class-1', joinCode: 'TESTCODE' }))
    vi.stubGlobal('fetch', fetchMock)
    const { getJoinCode } = await importApi({ bff: true })

    await getJoinCode('class-1')

    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/classes/class-1/join-code',
      expect.objectContaining({
        method: 'GET',
        credentials: 'same-origin',
        headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'join-code-csrf' }),
      })
    )
  })

  it('preserves Bearer authentication in legacy JWT mode', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)
    const { listCases, setAuthTokenGetter } = await importApi({ bff: false })
    setAuthTokenGetter(() => 'legacy.header.signature')

    await listCases()

    expect(fetchMock.mock.calls[0][1].headers.Authorization).toBe('Bearer legacy.header.signature')
    expect(fetchMock.mock.calls[0][1].credentials).toBeUndefined()
  })
})
