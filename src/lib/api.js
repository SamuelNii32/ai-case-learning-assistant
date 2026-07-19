import { API_BASE } from '@/config'
import { isDemoModeEnabled, isDemoSessionActive } from '@/auth/demoMode'
import { mockUploads, mockSessions, mockClassesMine, mockClassDetails, mockClassesEnrolled, mockNotes } from '@/mocks/demoMocks'
import { navigateTo } from './navigate'

// tokenGetter is a function that returns the current auth token (string|null).
// It can be registered by AuthContext so non-React modules can read the
// in-memory token. If not set, we fall back to reading localStorage.
let tokenGetter = null

// `authFailureHandler` is set by the app so the API helpers can notify the
// application about 401/403 events and take appropriate action (logout, UI)
let authFailureHandler = null

export function setRefreshTokenFn() {
  // Refresh flow disabled: ignore registration to prevent any refresh attempts
}

export function setAuthTokenGetter(fn) {
  tokenGetter = fn
}

export function setOnAuthFailure(fn) {
  authFailureHandler = fn
}

export function getAuthToken() {
  try {
    if (tokenGetter) return tokenGetter()
    if (typeof window !== 'undefined') return localStorage.getItem('authToken')
  } catch {
    return null
  }
}

// Optional hook that AuthContext can register so the API helpers can notify
// the app when the server rejects the token (401 or { error: 'invalid token' }).
// Note: `authFailureHandler` above is used by `handleAuthFailure`.

function makeUrl(path) {
  if (!API_BASE) return path.startsWith('/') ? path : `/${path}`
  const base = String(API_BASE).replace(/\/$/, '')
  return path.startsWith('/') ? `${base}${path}` : `${base}/${path}`
}

function authHeaders() {
  let token = null
  try {
    if (tokenGetter) {
      token = tokenGetter()
    } else if (typeof window !== 'undefined') {
      token = localStorage.getItem('authToken')
    }
  } catch {
    token = null
  }
  // Guard: only attach Authorization if token looks like a JWT (three segments)
  if (typeof token === 'string') {
    const parts = token.split('.')
    if (parts.length === 3 && parts[0] && parts[1] && parts[2]) {
      return { Authorization: `Bearer ${token}` }
    }
  }
  return {}
}

export function getPagedItems(data) {
  if (Array.isArray(data)) return data
  if (!data || typeof data !== 'object') return []

  const candidates = [
    data.items,
    data.results,
    data.data,
    data.sessions,
    data.notes,
    data.values,
  ]

  for (const candidate of candidates) {
    if (Array.isArray(candidate)) return candidate
  }

  if (data.data && typeof data.data === 'object') {
    return getPagedItems(data.data)
  }

  return []
}

export function getResponsePayload(data) {
  if (!data || typeof data !== 'object' || Array.isArray(data)) return data
  return data.data ?? data.summary ?? data.result ?? data
}

// (Deprecated) refresh handler registration removed. The codebase uses
// `setRefreshTokenFn` / `refreshTokenFn` for queued refresh semantics.

// Inspect the current token and determine if it's still fresh.
// Returns true when token exists and is not expired (with an optional
// threshold in seconds). Returns false when no token or token already
// expired/near-expiry. This helper does NOT perform a refresh — it only
// checks expiry so callers (especially streaming flows) can decide how to
// proceed when the token is expired.
export function ensureFreshToken(thresholdSeconds = 10) {
  void thresholdSeconds
  // Temporarily disable token refresh to avoid hitting missing /auth/refresh
  return true; // Always return true to indicate the token is fresh
}

// Note: doFetch wrapper removed in favor of requestWithRetry which is the
// primary helper used across the app. If a centralized fetch wrapper is
// desired later it can be reintroduced.

async function requestWithRetry(url, options = {}) {
  // ensure we call token getter at request time
  const headers = { ...(options.headers || {}), ...authHeaders() }
  const opts = { ...options, headers }

  // first attempt
  const res = await fetch(url, opts)
  if (res.status !== 401) return res

  // Refresh flow disabled — do not attempt refresh/retry on 401

  return res
}

export { requestWithRetry }

async function handleAuthFailure(res, body, endpoint) {
  try {
    if (authFailureHandler) {
      try {
        authFailureHandler({ status: res?.status, body, endpoint })
      } catch (err) {
        console.error('[api] authFailureHandler threw', err)
      }
    } else if (typeof window !== 'undefined') {
      // Fallback: redirect to sign in using SPA navigation if available
      try {
        navigateTo('/login', { replace: true })
      } catch {
        /* ignore */
      }
    }
  } catch (err) {
    console.error('[api] handleAuthFailure error', err)
  }
}

export async function buildIndex(uploadId) {
  const url = makeUrl(`/index/${encodeURIComponent(uploadId)}`)
  const res = await fetch(url, {
    method: 'POST',
    headers: { ...authHeaders() },
  })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error(`Indexing failed: ${res.status}`)
  }
  if (!res.ok) {
    const text = await res.text().catch(() => '')
    throw new Error(`Indexing failed: ${res.status} ${text}`)
  }
  return res.json()
}

export async function ask(uploadId, q) {
  const enc = encodeURIComponent(q || '')
  const url = makeUrl(`/ask/${encodeURIComponent(uploadId)}?q=${enc}`)
  const res = await requestWithRetry(url, { method: 'GET' })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/ask/${encodeURIComponent(uploadId)}?q=${enc}`)
    throw new Error(`Ask failed: ${res.status}`)
  }
  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Ask failed: ${res.status} ${txt}`)
  }
  return res.json()
}

export function pdfUrl(uploadId) {
  return makeUrl(`/uploads/${encodeURIComponent(uploadId)}.pdf`)
}

export async function pagesPreview(uploadId) {
  const url = makeUrl(`/uploads/${encodeURIComponent(uploadId)}/pages/preview`)
  const res = await requestWithRetry(url, { method: 'GET' })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/uploads/${encodeURIComponent(uploadId)}/pages/preview`)
    throw new Error('Preview fetch failed: unauthorized')
  }
  if (!res.ok) throw new Error('Preview fetch failed')
  return res.json()
}

export async function listCases() {
  const url = makeUrl('/uploads/mine')
  if (isDemoModeEnabled() && isDemoSessionActive()) {
    return Promise.resolve(mockUploads)
  }
  const res = await requestWithRetry(url, { method: 'GET' })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, '/uploads/mine')
    throw new Error('List cases failed: unauthorized')
  }
  if (!res.ok) throw new Error('List cases failed')
  return res.json()
}

export async function uploadFile(formData) {
  const url = makeUrl('/uploads')
  const res = await requestWithRetry(url, { method: 'POST', body: formData })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, '/uploads')
    throw new Error(`Upload failed (${res.status}): ${txt}`)
  }
  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Upload failed (${res.status}): ${txt}`)
  }
  return res.json()
}

export async function getUploadSummary(uploadId) {
  const url = makeUrl(`/uploads/${encodeURIComponent(uploadId)}/summary`)
  const res = await requestWithRetry(url, { method: 'GET' })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/uploads/${encodeURIComponent(uploadId)}/summary`)
    throw new Error(`Summary failed (${res.status})`)
  }
  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Summary failed (${res.status}): ${txt}`)
  }
  return res.json()
}

export async function getUploadLayout(uploadId) {
  const path = `/uploads/${encodeURIComponent(uploadId)}/layout`
  let res = await requestWithRetry(makeUrl(path), { method: 'GET' })

  if (res.status === 404) {
    const analyzePath = `/uploads/${encodeURIComponent(uploadId)}/layout/analyze`
    const analyzeRes = await requestWithRetry(makeUrl(analyzePath), { method: 'POST' })
    if (analyzeRes.status === 401) {
      const txt = await analyzeRes.text().catch(() => '')
      await handleAuthFailure(analyzeRes, txt, analyzePath)
      throw new Error('Layout analysis failed: unauthorized')
    }
    if (!analyzeRes.ok) {
      const txt = await analyzeRes.text().catch(() => '')
      throw new Error(`Layout analysis failed: ${analyzeRes.status} ${txt}`)
    }
    return analyzeRes.json()
  }

  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, path)
    throw new Error('Layout fetch failed: unauthorized')
  }
  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Layout fetch failed: ${res.status} ${txt}`)
  }
  return res.json()
}

export async function startReadingCoach(uploadId) {
  const url = makeUrl(`/tutor/reading/start/${encodeURIComponent(uploadId)}`)
  const res = await requestWithRetry(url, { method: 'POST' })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/tutor/reading/start/${encodeURIComponent(uploadId)}`)
    throw new Error('Reading Coach failed: unauthorized')
  }
  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Reading Coach failed: ${res.status} ${txt}`)
  }
  return res.json()
}

export async function answerReadingCoach(sessionId, stepId, answer) {
  const url = makeUrl('/tutor/reading/answer')
  const res = await requestWithRetry(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sessionId, stepId, answer }),
  })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, '/tutor/reading/answer')
    throw new Error('Reading Coach answer failed: unauthorized')
  }
  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Reading Coach answer failed: ${res.status} ${txt}`)
  }
  return res.json()
}

export async function createSession(uploadId) {
  const url = makeUrl('/sessions')
  const res = await requestWithRetry(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ uploadId }),
  })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, '/sessions')
    throw new Error(`Create session failed: unauthorized`)
  }
  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Create session failed: ${res.status} ${txt}`)
  }
  return res.json()
}

function createHttpError(message, status, bodyText = '') {
  const err = new Error(message)
  err.status = status
  err.body = bodyText
  return err
}

export async function startTutor(sessionId, uploadId) {
  const url = makeUrl(`/tutor/start/${encodeURIComponent(uploadId)}`)
  const res = await requestWithRetry(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sessionId, uploadId }),
  })

  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/tutor/start/${encodeURIComponent(uploadId)}`)
    throw createHttpError('Tutor start failed: unauthorized', 401, txt)
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw createHttpError(`Tutor start failed: ${res.status} ${txt}`.trim(), res.status, txt)
  }

  return res.json()
}

export async function stepTutor(sessionId, choiceId) {
  const url = makeUrl('/tutor/step')
  const res = await requestWithRetry(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sessionId, choiceId }),
  })

  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, '/tutor/step')
    throw createHttpError('Tutor step failed: unauthorized', 401, txt)
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw createHttpError(`Tutor step failed: ${res.status} ${txt}`.trim(), res.status, txt)
  }

  return res.json()
}

// -----------------------------
// Reading Coach API helpers
// -----------------------------

export async function getReadingResume(uploadId) {
  const url = makeUrl(`/tutor/reading/resume/${encodeURIComponent(uploadId)}`)
  const res = await requestWithRetry(url, { method: 'GET' })

  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/tutor/reading/resume/${encodeURIComponent(uploadId)}`)
    throw createHttpError('Reading resume failed: unauthorized', 401, txt)
  }

  if (res.status === 404) {
    // Expose body for callers to inspect canStart flag
    const txt = await res.text().catch(() => '')
    const err = createHttpError('Reading resume: not found', 404, txt)
    throw err
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw createHttpError(`Reading resume failed: ${res.status} ${txt}`.trim(), res.status, txt)
  }

  return res.json()
}

export async function startReading(uploadId) {
  const url = makeUrl(`/tutor/reading/start/${encodeURIComponent(uploadId)}`)
  const res = await requestWithRetry(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ uploadId }),
  })

  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/tutor/reading/start/${encodeURIComponent(uploadId)}`)
    throw createHttpError('Reading start failed: unauthorized', 401, txt)
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw createHttpError(`Reading start failed: ${res.status} ${txt}`.trim(), res.status, txt)
  }

  return res.json()
}

export async function submitReadingAnswer(sessionId, stepId, answer) {
  const url = makeUrl('/tutor/reading/answer')
  const res = await requestWithRetry(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sessionId, stepId, answer }),
  })

  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, '/tutor/reading/answer')
    throw createHttpError('Reading answer failed: unauthorized', 401, txt)
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw createHttpError(`Reading answer failed: ${res.status} ${txt}`.trim(), res.status, txt)
  }

  return res.json()
}

export async function listSessionsMine() {
  const url = makeUrl('/sessions/mine')
  if (isDemoModeEnabled() && isDemoSessionActive()) {
    return Promise.resolve(mockSessions)
  }
  const res = await requestWithRetry(url, { method: 'GET' })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, '/sessions/mine')
    throw new Error('Sessions fetch failed: unauthorized')
  }
  if (!res.ok) throw new Error('Sessions fetch failed')
  const data = await res.json()
  return getPagedItems(data)
}

export async function listSessionNotes(sessionId) {
  const url = makeUrl(`/sessions/${encodeURIComponent(sessionId)}/notes`)
  if (isDemoModeEnabled() && isDemoSessionActive()) {
    return Promise.resolve(mockNotes)
  }
  const res = await requestWithRetry(url, { method: 'GET' })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/sessions/${encodeURIComponent(sessionId)}/notes`)
    throw new Error('Notes fetch failed: unauthorized')
  }
  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Notes fetch failed: ${res.status} ${txt}`)
  }
  const data = await res.json()
  return getPagedItems(data)
}

export async function getSession(sessionId) {
  const url = makeUrl(`/sessions/${encodeURIComponent(sessionId)}`)
  if (isDemoModeEnabled() && isDemoSessionActive()) {
    const s = mockSessions.find(x => String(x.id) === String(sessionId)) || mockSessions[0]
    return Promise.resolve(s)
  }
  const res = await requestWithRetry(url, { method: 'GET' })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/sessions/${encodeURIComponent(sessionId)}`)
    throw new Error('Session fetch failed: unauthorized')
  }
  if (!res.ok) throw new Error('Session fetch failed')
  return res.json()
}

export function streamFetch(path, options = {}) {
  // Ensure token is present and not expired before starting a long-lived
  // streaming request. Callers should handle the thrown error and show a
  // re-auth prompt when needed.
  const url = path.startsWith('http') ? path : makeUrl(path)
  return (async () => {
    const ok = ensureFreshToken()
    if (!ok) {
      throw new Error('auth:expired')
    }
    // requestWithRetry will attach auth headers and retry once on 401.
    return await requestWithRetry(url, { ...options })
  })()
}

export async function renameCase(uploadId, name) {
  const url = makeUrl(`/uploads/${encodeURIComponent(uploadId)}/name`)
  const res = await requestWithRetry(url, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      ...authHeaders(),
    },
    credentials: 'include',
    // Send both `name` and `title` to be tolerant of backend DTO shape
    body: JSON.stringify({ name, title: name }),
  })

  // Try again with requestWithRetry to handle token refresh
  // (some environments may not support streaming retry, so we fall back)
  // Note: requestWithRetry will call the registered refresh function if available
  // and retry the request once.
  // If fetch above succeeded, `res` already holds the response; otherwise we proceed.

  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/uploads/${encodeURIComponent(uploadId)}/name`)
    throw new Error(`Rename failed (${res.status})`)
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Rename failed (${res.status}): ${txt}`)
  }

  // Some backends may return 204 No Content. Try to parse JSON, but
  // fall back to a sensible shape if there's no body.
  try {
    return await res.json()
  } catch {
    return { uploadId, name }
  }
}

export async function deleteCase(uploadId) {
  const url = makeUrl(`/uploads/${encodeURIComponent(uploadId)}`)
  const res = await requestWithRetry(url, {
    method: 'DELETE',
    headers: {
      ...authHeaders(),
    },
    credentials: 'include',
  })

  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/uploads/${encodeURIComponent(uploadId)}`)
    throw new Error(`Delete failed (${res.status})`)
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Delete failed (${res.status}): ${txt}`)
  }

  return res.json() // { uploadId, deleted: true }
}

export async function getSessionNotes(sessionId) {
  const url = makeUrl(`/sessions/${encodeURIComponent(sessionId)}/notes`)
  const res = await requestWithRetry(url, {
    headers: { ...authHeaders() },
    credentials: 'include',
  })

  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/sessions/${encodeURIComponent(sessionId)}/notes`)
    throw new Error('Notes fetch failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Notes fetch failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function addSessionNote(sessionId, text) {
  const url = makeUrl(`/sessions/${encodeURIComponent(sessionId)}/notes`)
  const res = await requestWithRetry(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...authHeaders(),
    },
    credentials: 'include',
    body: JSON.stringify({ text }),
  })

  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/sessions/${encodeURIComponent(sessionId)}/notes`)
    throw new Error('Add note failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Add note failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function updateSessionNote(sessionId, noteId, text) {
  const url = makeUrl(
    `/sessions/${encodeURIComponent(sessionId)}/notes/${encodeURIComponent(noteId)}`
  )
  const res = await requestWithRetry(url, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      ...authHeaders(),
    },
    credentials: 'include',
    body: JSON.stringify({ text }),
  })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/sessions/${encodeURIComponent(sessionId)}/notes/${encodeURIComponent(noteId)}`)
    throw new Error('Update note failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Update note failed (${res.status}): ${txt}`)
  }

  try {
    return await res.json()
  } catch {
    return { sessionId, noteId, text }
  }
}

export async function deleteSessionNote(sessionId, noteId) {
  const url = makeUrl(
    `/sessions/${encodeURIComponent(sessionId)}/notes/${encodeURIComponent(noteId)}`
  )
  const res = await requestWithRetry(url, {
    method: 'DELETE',
    headers: { ...authHeaders() },
    credentials: 'include',
  })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/sessions/${encodeURIComponent(sessionId)}/notes/${encodeURIComponent(noteId)}`)
    throw new Error('Delete note failed: unauthorized')
  }

  if (res.status === 404) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Delete note failed: not found (${txt || ''})`)
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Delete note failed (${res.status}): ${txt}`)
  }

  return true
}

export async function deleteSession(sessionId) {
  const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
  const url = `${base}/sessions/${encodeURIComponent(sessionId)}`

  const token = tokenGetter ? tokenGetter() : null
  const headers = token ? { Authorization: `Bearer ${token}` } : {}

  const res = await requestWithRetry(url, { method: 'DELETE', headers })

  if (res.status === 401 || res.status === 403) {
    authFailureHandler?.({ where: 'deleteSession', status: res.status })
    throw new Error('Please sign in again to delete this conversation.')
  }

  if (res.status === 404) {
    throw new Error('This conversation was already deleted or is no longer available.')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(txt || 'Delete conversation failed. Please try again.')
  }

  return true
}

// --- Class Management (Instructor only) ---

export async function getClasses() {
  // Backend exposes instructor-owned classes at /classes/mine
  const url = makeUrl('/classes/mine')
  if (isDemoModeEnabled() && isDemoSessionActive()) {
    return Promise.resolve(mockClassesMine)
  }
  const res = await requestWithRetry(url, {
    method: 'GET',
  })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Get classes failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Get classes failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function createClass(name, description = '') {
  const url = makeUrl('/classes')
  const res = await requestWithRetry(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ name, description }),
  })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Create class failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Create class failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function getClassDetails(classId) {
  const url = makeUrl(`/classes/${encodeURIComponent(classId)}/details`)
  if (isDemoModeEnabled() && isDemoSessionActive()) {
    return Promise.resolve(mockClassDetails)
  }
  const res = await requestWithRetry(url, { method: 'GET' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Get class details failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Get class details failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function deleteClass(classId) {
  const url = makeUrl(`/classes/${encodeURIComponent(classId)}`)
  const res = await requestWithRetry(url, { method: 'DELETE' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Delete class failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Delete class failed (${res.status}): ${txt}`)
  }

  try {
    return await res.json()
  } catch {
    return true
  }
}

export async function getClassStudents(classId) {
  const url = makeUrl(`/classes/${encodeURIComponent(classId)}/students`)
  if (isDemoModeEnabled() && isDemoSessionActive()) {
    return Promise.resolve(mockClassDetails.students)
  }
  const res = await requestWithRetry(url, { method: 'GET' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Get class students failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Get class students failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function getClassCases(classId) {
  const url = makeUrl(`/classes/${encodeURIComponent(classId)}/cases`)
  if (isDemoModeEnabled() && isDemoSessionActive()) {
    return Promise.resolve(mockClassDetails.cases)
  }
  const res = await requestWithRetry(url, { method: 'GET' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Get class cases failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Get class cases failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function getClassTutorProgress(classId) {
  const url = makeUrl(`/admin/classes/${encodeURIComponent(classId)}/tutor-progress`)
  const res = await requestWithRetry(url, { method: 'GET' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, `/admin/classes/${encodeURIComponent(classId)}/tutor-progress`)
    throw new Error('Get Reading Coach progress failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Get Reading Coach progress failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function getClassReadingCoachSummary(classId) {
  const path = `/admin/classes/${encodeURIComponent(classId)}/tutor-summary`
  const url = makeUrl(path)
  const res = await requestWithRetry(url, { method: 'GET' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, path)
    throw new Error('Get Reading Coach summary failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Get Reading Coach summary failed (${res.status}): ${txt}`)
  }

  return getResponsePayload(await res.json())
}

export async function getStudentTutorProgress(classId, studentId, uploadId) {
  const url = makeUrl(
    `/admin/classes/${encodeURIComponent(classId)}/tutor-progress/${encodeURIComponent(studentId)}/${encodeURIComponent(uploadId)}`
  )
  const res = await requestWithRetry(url, { method: 'GET' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt, url)
    throw new Error('Get learner Reading Coach detail failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Get learner Reading Coach detail failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function addStudentToClass(classId, studentEmail) {
  const url = makeUrl(`/classes/${encodeURIComponent(classId)}/students`)
  const res = await requestWithRetry(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ studentEmail }),
  })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Add student failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Add student failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function assignCaseToClass(classId, assignment) {
  const url = makeUrl(`/classes/${encodeURIComponent(classId)}/cases`)
  const payload =
    typeof assignment === 'string'
      ? { uploadId: assignment, readingCoachQuestions: '' }
      : {
          ...assignment,
          uploadId: assignment?.uploadId,
          readingCoachQuestions: assignment?.readingCoachQuestions ?? '',
        }

  const res = await requestWithRetry(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Assign case failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Assign case failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function unassignCaseFromClass(classId, uploadId) {
  const url = makeUrl(
    `/classes/${encodeURIComponent(classId)}/cases/${encodeURIComponent(uploadId)}`
  )
  const res = await requestWithRetry(url, { method: 'DELETE' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Unassign case failed: unauthorized')
  }

  if (res.status === 404) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Unassign case failed: not found (${txt || 'assignment not found'})`)
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Unassign case failed (${res.status}): ${txt}`)
  }

  // Prefer boolean true for 204; pass through body if present
  try {
    const body = await res.json()
    return body ?? true
  } catch {
    return true
  }
}

export async function getClassHistory(classId) {
  const url = makeUrl(`/classes/${encodeURIComponent(classId)}/history`)
  const res = await requestWithRetry(url, { method: 'GET' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Get class history failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Get class history failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function getClassSession(classId, sessionId) {
  const url = makeUrl(
    `/classes/${encodeURIComponent(classId)}/sessions/${encodeURIComponent(sessionId)}`
  )
  const res = await requestWithRetry(url, { method: 'GET' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Get class session failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Get class session failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function getMyUploads() {
  const url = makeUrl('/uploads/mine')
  if (isDemoModeEnabled() && isDemoSessionActive()) {
    return Promise.resolve(mockUploads)
  }
  const res = await requestWithRetry(url, { method: 'GET' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Get uploads failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Get uploads failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function getEnrolledClasses() {
  const url = makeUrl('/classes/enrolled')
  if (isDemoModeEnabled() && isDemoSessionActive()) {
    return Promise.resolve(mockClassesEnrolled)
  }
  const res = await requestWithRetry(url, { method: 'GET' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Get enrolled classes failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Get enrolled classes failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function deleteStudentFromClass(classId, studentId) {
  const url = makeUrl(
    `/classes/${encodeURIComponent(classId)}/students/${encodeURIComponent(studentId)}`
  )
  const res = await requestWithRetry(url, { method: 'DELETE' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Remove student failed: unauthorized')
  }

  if (res.status === 404) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Remove student failed: not found (${txt || 'enrollment not found'})`)
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Remove student failed (${res.status}): ${txt}`)
  }

  // Many backends return 204 No Content for DELETE.
  try {
    const body = await res.json()
    // if server returns a body, pass it through
    return body ?? true
  } catch {
    return true
  }
}

// Class Join Codes
// -----------------------------------------------

export async function getJoinCode(classId) {
  const url = makeUrl(`/classes/${encodeURIComponent(classId)}/join-code`)
  const res = await requestWithRetry(url, { method: 'GET' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Get join code failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Get join code failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function regenerateJoinCode(classId) {
  const url = makeUrl(`/classes/${encodeURIComponent(classId)}/join-code/regenerate`)
  const res = await requestWithRetry(url, { method: 'POST' })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Regenerate join code failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Regenerate join code failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function joinClass(joinCode) {
  const url = makeUrl('/classes/join')
  const res = await requestWithRetry(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ joinCode }),
  })

  if (res.status === 401 || res.status === 403) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Join class failed: unauthorized')
  }

  if (res.status === 404) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Join class failed: class not found (${txt || 'invalid join code'})`)
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Join class failed (${res.status}): ${txt}`)
  }

  return res.json()
}
