import React, { createContext, useState, useEffect, useCallback } from 'react'
import { setAuthTokenGetter, setOnAuthFailure } from '@/lib/api'
// at the top of AuthContext.jsx
import { API_BASE } from '@/config'

const AuthContext = createContext(null)
export { AuthContext }

function AuthProvider({ children }) {
  const [token, setToken] = useState(() => {
    try {
      return typeof window !== 'undefined' ? localStorage.getItem('authToken') || null : null
    } catch {
      return null
    }
  })

  const [user, setUser] = useState(() => {
    try {
      const raw = typeof window !== 'undefined' ? localStorage.getItem('authUser') : null
      return raw ? JSON.parse(raw) : null
    } catch {
      return null
    }
  })

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

  // When a token exists, validate it once against the backend.
  // If the backend says 401, clear it so the UI doesn't pretend we're logged in.
  useEffect(() => {
    if (!token) return

    let cancelled = false

    ;(async () => {
      try {
        const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
        const url = `${base}/uploads/mine`

        const res = await fetch(url, {
          headers: {
            Authorization: `Bearer ${token}`,
          },
        })

        if (!res.ok && res.status === 401 && !cancelled) {
          console.warn('[auth] startup token check: backend says 401, logging out')
          logout()
          // optional: also send them to sign-in page
          // if (typeof window !== 'undefined') {
          //   window.location.href = '/signin'
          // }
        }
      } catch (err) {
        // Network down / backend not running — don't log out, just log it.
        console.warn('[auth] startup token check failed (network?)', err)
      }
    })()

    return () => {
      cancelled = true
    }
  }, [token, logout])

  const value = {
    token,
    user,
    // 🔑 Only consider the user logged in if we actually have a token.
    loggedIn: Boolean(token),
    login,
    logout,
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export default AuthProvider
