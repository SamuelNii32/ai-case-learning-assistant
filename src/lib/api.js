import { API_BASE } from '@/config'

// tokenGetter is a function that returns the current auth token (string|null).
// It can be registered by AuthContext so non-React modules can read the
// in-memory token. If not set, we fall back to reading localStorage.
let tokenGetter = null

// `authFailureHandler` is set by the app so the API helpers can notify the
// application about 401/403 events and take appropriate action (logout, UI)
let authFailureHandler = null

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
    }
  } catch {
    token = null
  }
  return token ? { Authorization: `Bearer ${token}` } : {}
}

async function handleAuthFailure(res, body) {
  try {
    if (authFailureHandler) {
      try {
        authFailureHandler({ status: res?.status, body })
      } catch (err) {
        console.error('[api] authFailureHandler threw', err)
      }
    } else if (typeof window !== 'undefined') {
      // Fallback: redirect to sign in
      try {
        window.location.href = '/signin'
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
  const res = await fetch(url, {
    headers: { ...authHeaders() },
  })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
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
  const res = await fetch(url, {
    headers: { ...authHeaders() },
  })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Preview fetch failed: unauthorized')
  }
  if (!res.ok) throw new Error('Preview fetch failed')
  return res.json()
}

export async function listCases() {
  const url = makeUrl('/uploads/mine')
  const res = await fetch(url, { headers: { ...authHeaders() } })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('List cases failed: unauthorized')
  }
  if (!res.ok) throw new Error('List cases failed')
  return res.json()
}

export async function uploadFile(formData) {
  const url = makeUrl('/uploads')
  const res = await fetch(url, {
    method: 'POST',
    body: formData,
    headers: { ...authHeaders() },
  })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
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
  const res = await fetch(url, {
    headers: { ...authHeaders() },
  })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error(`Summary failed (${res.status})`)
  }
  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Summary failed (${res.status}): ${txt}`)
  }
  return res.json()
}

export async function createSession(uploadId) {
  const url = makeUrl('/sessions')
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify({ uploadId }),
  })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error(`Create session failed: unauthorized`)
  }
  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Create session failed: ${res.status} ${txt}`)
  }
  return res.json()
}

export async function listSessionsMine() {
  const url = makeUrl('/sessions/mine')
  const res = await fetch(url, {
    headers: { ...authHeaders() },
  })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Sessions fetch failed: unauthorized')
  }
  if (!res.ok) throw new Error('Sessions fetch failed')
  return res.json()
}

export async function listSessionNotes(sessionId) {
  const url = makeUrl(`/sessions/${encodeURIComponent(sessionId)}/notes`)
  const res = await fetch(url, {
    headers: { ...authHeaders() },
  })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Notes fetch failed: unauthorized')
  }
  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Notes fetch failed: ${res.status} ${txt}`)
  }
  return res.json()
}

export async function getSession(sessionId) {
  const url = makeUrl(`/sessions/${encodeURIComponent(sessionId)}`)
  const res = await fetch(url, {
    headers: { ...authHeaders() },
  })
  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
    throw new Error('Session fetch failed: unauthorized')
  }
  if (!res.ok) throw new Error('Session fetch failed')
  return res.json()
}

export function streamFetch(path, options = {}) {
  const url = path.startsWith('http') ? path : makeUrl(path)
  const headers = { ...(options.headers || {}), ...authHeaders() }
  return fetch(url, { ...options, headers })
}

export async function renameCase(uploadId, name) {
  const url = makeUrl(`/uploads/${encodeURIComponent(uploadId)}/name`)
  const res = await fetch(url, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      ...authHeaders(),
    },
    credentials: 'include',
    // Send both `name` and `title` to be tolerant of backend DTO shape
    body: JSON.stringify({ name, title: name }),
  })

  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
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
  const res = await fetch(url, {
    method: 'DELETE',
    headers: {
      ...authHeaders(),
    },
    credentials: 'include',
  })

  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
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
  const res = await fetch(url, {
    headers: { ...authHeaders() },
    credentials: 'include',
  })

  if (res.status === 401) {
    const txt = await res.text().catch(() => '')
    await handleAuthFailure(res, txt)
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
  const res = await fetch(url, {
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
    await handleAuthFailure(res, txt)
    throw new Error('Add note failed: unauthorized')
  }

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(`Add note failed (${res.status}): ${txt}`)
  }

  return res.json()
}

export async function deleteSession(sessionId) {
  const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
  const url = `${base}/sessions/${encodeURIComponent(sessionId)}`

  const token = tokenGetter ? tokenGetter() : null
  const headers = token ? { Authorization: `Bearer ${token}` } : {}

  const res = await fetch(url, { method: 'DELETE', headers })

  if (res.status === 401 || res.status === 403) {
    authFailureHandler?.({ where: 'deleteSession', status: res.status })
    throw new Error('unauthorized')
  }

  if (!res.ok) {
    throw new Error('Delete session failed')
  }

  return true
}
