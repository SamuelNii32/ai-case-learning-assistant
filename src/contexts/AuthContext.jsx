import React, { createContext, useState, useEffect, useCallback } from 'react'
import { setAuthTokenGetter, setOnAuthFailure } from '@/lib/api'
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
      console.warn('[auth] token invalid or expired; logging out', info)
      // Backend has rejected this token: clear it immediately
      logout()
      // Optional: you can show a toast here instead of alert
      // toast.error('Session expired. Please sign in again.')
    })
  }, [token, logout])

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
          isSuperUser: !!js.isSuperUser,
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
