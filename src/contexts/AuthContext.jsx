import React, { createContext, useState, useEffect, useCallback } from 'react'
import { setAuthTokenGetter, setOnAuthFailure, setRefreshTokenFn } from '@/lib/api'
// at the top of AuthContext.jsx
import { API_BASE } from '@/config'

const AuthContext = createContext(null)
export { AuthContext }

function AuthProvider({ children }) {
  const [token, setToken] = useState(() => {
    if (typeof window === 'undefined') return null
    try {
      return localStorage.getItem('authToken') || null
    } catch {
      return null
    }
  })

  // On startup, we don't trust the stored user; we’ll re-fetch it via /me.
  // So begin with null and let login()/`/me` fill it.
  const [user, setUser] = useState(null)

  const login = useCallback((newToken, newUser) => {
    try {
      if (typeof window !== 'undefined') {
        if (newToken) localStorage.setItem('authToken', newToken)
        if (newUser) localStorage.setItem('authUser', JSON.stringify(newUser))
      }
    } catch {
      /* ignore */
    }
    setToken(newToken || null)
    setUser(newUser || null)
  }, [])

  const logout = useCallback(() => {
    try {
      if (typeof window !== 'undefined') {
        localStorage.removeItem('authToken')
        localStorage.removeItem('authUser')
      }
    } catch {
      /* ignore */
    }
    setToken(null)
    setUser(null)
  }, [])

  // Let api.js read the current token + react to invalid token / 401
  useEffect(() => {
    setAuthTokenGetter(() => token)

    setOnAuthFailure(info => {
      // Always log for visibility
      console.warn('[auth] auth failure', info)

      // Only force logout on 401 from the auth-critical endpoint
      if (info?.status === 401 && info?.endpoint === '/me') {
        logout()
        ;(async () => {
          try {
            const { navigateTo } = await import('@/lib/navigate')
            if (typeof navigateTo === 'function') navigateTo('/login', { replace: true })
            else window.location.assign('/login')
          } catch {
            try {
              window.location.assign('/login')
            } catch {
              /* ignore */
            }
          }
        })()
      }
      // For other 401s, do not logout; let UI handle the error normally
    })
  }, [token, logout])

  // Register a refresh function with api.js so API helpers can attempt a
  // refresh when they receive a 401. This implements the queued-refresh
  // pattern: concurrent callers will await the same in-flight refresh.
  useEffect(() => {
    let refreshingRef = { current: null }

    async function refreshAuthToken() {
      // If a refresh is already running, return the same promise
      if (refreshingRef.current) return refreshingRef.current

      // Create a refresh promise and store it
      refreshingRef.current = (async () => {
        try {
          const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
          const url = base ? `${base}/auth/refresh` : '/auth/refresh'
          const res = await fetch(url, { method: 'POST', credentials: 'include' })
          if (!res.ok) throw new Error('refresh failed')
          const js = await res.json().catch(() => ({}))
          const newToken = js?.accessToken || js?.token || js?.authToken || null
          if (newToken) {
            try {
              if (typeof window !== 'undefined') localStorage.setItem('authToken', newToken)
            } catch {
              /* ignore */
            }
            setToken(newToken)
            return newToken
          }
          throw new Error('no token in refresh response')
        } catch (err) {
          // On refresh failure, logout to force re-auth
          try {
            logout()
          } catch {
            /* ignore */
          }
          throw err
        } finally {
          refreshingRef.current = null
        }
      })()

      return refreshingRef.current
    }

    try {
      setRefreshTokenFn(refreshAuthToken)
    } catch (err) {
      console.error('[auth] failed to register refresh function with api helper', err)
    }

    return () => {
      try {
        setRefreshTokenFn(null)
      } catch {
        /* ignore */
      }
    }
  }, [logout])

  // Keep auth state in sync across tabs/windows, but ONLY on authToken/authUser
  useEffect(() => {
    function onStorage(e) {
      try {
        if (!e) return
        if (e.key === 'authToken') {
          const newToken = typeof window !== 'undefined' ? localStorage.getItem('authToken') : null
          setToken(newToken || null)
        }
        if (e.key === 'authUser') {
          const raw = typeof window !== 'undefined' ? localStorage.getItem('authUser') : null
          try {
            setUser(raw ? JSON.parse(raw) : null)
          } catch {
            setUser(null)
          }
        }
      } catch {
        /* ignore */
      }
    }

    if (typeof window !== 'undefined') {
      window.addEventListener('storage', onStorage)
    }
    return () => {
      try {
        if (typeof window !== 'undefined') {
          window.removeEventListener('storage', onStorage)
        }
      } catch {
        /* ignore */
      }
    }
  }, [])

  useEffect(() => {
    if (!token) return

    let cancelled = false

    ;(async () => {
      try {
        const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
        const url = base ? `${base}/me` : '/me'

        const res = await fetch(url, {
          headers: {
            Authorization: `Bearer ${token}`,
          },
        })

        if (!res.ok) {
          if (res.status === 401 || res.status === 403) {
            console.warn('[auth] /me says token is invalid; logging out')
            logout()
          } else {
            console.warn('[auth] /me failed with status', res.status)
          }
          return
        }

        const js = await res.json()

        if (cancelled) return

        const normalizedUser = {
          userId: js.userId || js.id || null,
          email: js.email || null,
          fullName: js.fullName || null,
          // Prefer an explicit role string from the server. Fall back to
          // legacy boolean `isSuperUser` when present so older backends
          // remain compatible during a rollout.
          role: js.role || (js.isSuperUser ? 'instructor' : 'student'),
        }

        setUser(normalizedUser)

        // Keep localStorage in sync (optional, but nice)
        try {
          if (typeof window !== 'undefined') {
            localStorage.setItem('authUser', JSON.stringify(normalizedUser))
          }
        } catch {
          /* ignore */
        }
      } catch (err) {
        if (!cancelled) {
          console.error('[auth] /me error', err)
        }
      }
    })()

    return () => {
      cancelled = true
    }
  }, [token, logout])

  const value = {
    token,
    user,
    loggedIn: Boolean(token && user),
    login,
    logout,
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export default AuthProvider
