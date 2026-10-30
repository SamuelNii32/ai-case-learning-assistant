import React, { createContext, useCallback, useEffect, useMemo, useState } from 'react'
import { setAuthTokenGetter, setOnAuthFailure, setRefreshTokenFn } from '@/lib/api'
import { API_BASE } from '@/config'
import { AUTH_MODE } from '@/auth/authMode'
import {
  isDemoModeEnabled,
  isDemoSessionActive,
  getDemoUser,
  endDemoSession,
} from '@/auth/demoMode'

const AuthContext = createContext(null)
export { AuthContext }

function normalizeUser(value) {
  return {
    userId: value?.userId || value?.id || null,
    email: value?.email || null,
    fullName: value?.fullName || null,
    role: value?.role || (value?.isSuperUser ? 'instructor' : 'student'),
  }
}

function localUrl(path) {
  return path.startsWith('/') ? path : `/${path}`
}

function legacyUrl(path) {
  const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
  return base ? `${base}${localUrl(path)}` : localUrl(path)
}

function navigateBrowserTopLevel(url) {
  window.location.assign(url)
}

function AuthProvider({ children, navigateTopLevel = navigateBrowserTopLevel }) {
  const isBff = AUTH_MODE === 'bff'
  const [token, setToken] = useState(() => {
    if (isBff || typeof window === 'undefined') return null
    try {
      return localStorage.getItem('authToken') || null
    } catch {
      return null
    }
  })
  const [user, setUser] = useState(null)
  const [authStatus, setAuthStatus] = useState(isBff ? 'checking' : 'ready')

  const clearBffState = useCallback(() => {
    setToken(null)
    setUser(null)
    setAuthStatus('unauthenticated')
  }, [])

  const login = useCallback(
    (newToken, newUser) => {
      if (isBff) return
      try {
        if (typeof window !== 'undefined') {
          if (newToken) localStorage.setItem('authToken', newToken)
          if (newUser) localStorage.setItem('authUser', JSON.stringify(newUser))
        }
      } catch {
        /* storage can be unavailable */
      }
      setToken(newToken || null)
      setUser(newUser || null)
    },
    [isBff]
  )

  const restoreBffSession = useCallback(async () => {
    if (!isBff) return
    setAuthStatus('checking')
    setUser(null)

    try {
      const sessionResponse = await fetch('/bff/session', { credentials: 'same-origin' })
      if (!sessionResponse.ok) throw new Error(`Session check failed (${sessionResponse.status})`)
      const session = await sessionResponse.json()

      if (!session?.authenticated) {
        clearBffState()
        return
      }
      if (!session?.accountLinked) {
        if (session?.status === 'account_disabled') setAuthStatus('suspended')
        else setAuthStatus(session?.status === 'invalid_mapping' ? 'invalid_mapping' : 'unlinked')
        return
      }

      const meResponse = await fetch('/me', { credentials: 'same-origin' })
      if (meResponse.status === 401 || meResponse.status === 403) {
        clearBffState()
        return
      }
      if (!meResponse.ok) throw new Error(`Account lookup failed (${meResponse.status})`)

      setUser(normalizeUser(await meResponse.json()))
      setAuthStatus('authenticated')
    } catch (error) {
      console.error('[auth] BFF session restoration failed', error)
      setAuthStatus('error')
    }
  }, [clearBffState, isBff])

  const loginWithAuth0 = useCallback(
    (returnUrl = '/login') => {
      if (!isBff || typeof window === 'undefined') return
      const safeReturnUrl = String(returnUrl).startsWith('/') ? returnUrl : '/login'
      window.location.assign(`/bff/login?returnUrl=${encodeURIComponent(safeReturnUrl)}`)
    },
    [isBff]
  )

  const logout = useCallback(async () => {
    if (isBff) {
      clearBffState()
      try {
        const csrfResponse = await fetch('/bff/csrf', { credentials: 'same-origin' })
        if (!csrfResponse.ok) return
        const { requestToken } = await csrfResponse.json()
        if (!requestToken) return
        const logoutResponse = await fetch('/bff/logout/prepare?returnUrl=/login', {
          method: 'POST',
          credentials: 'same-origin',
          headers: { 'X-CSRF-TOKEN': requestToken },
        })
        if (!logoutResponse.ok) return
        const { logoutUrl } = await logoutResponse.json()
        if (typeof logoutUrl === 'string' && logoutUrl.startsWith('/bff/logout/complete?')) {
          navigateTopLevel(logoutUrl)
        }
      } catch (error) {
        console.error('[auth] BFF logout failed', error)
      }
      return
    }

    try {
      if (typeof window !== 'undefined') {
        localStorage.removeItem('authToken')
        localStorage.removeItem('authUser')
        endDemoSession()
      }
    } catch {
      /* storage can be unavailable */
    }
    setToken(null)
    setUser(null)
  }, [clearBffState, isBff, navigateTopLevel])

  useEffect(() => {
    setAuthTokenGetter(() => (isBff ? null : token))
    setOnAuthFailure(info => {
      console.warn('[auth] auth failure', info)
      if (info?.status !== 401) return
      if (isBff) {
        clearBffState()
      } else if (info?.endpoint === '/me') {
        void logout()
      }
    })
    return () => {
      setAuthTokenGetter(null)
      setOnAuthFailure(null)
    }
  }, [clearBffState, isBff, logout, token])

  useEffect(() => {
    setRefreshTokenFn(null)
  }, [])

  useEffect(() => {
    if (isBff) return undefined
    function onStorage(event) {
      if (event?.key === 'authToken') setToken(localStorage.getItem('authToken') || null)
      if (event?.key === 'authUser') {
        try {
          setUser(JSON.parse(localStorage.getItem('authUser') || 'null'))
        } catch {
          setUser(null)
        }
      }
    }
    window.addEventListener('storage', onStorage)
    return () => window.removeEventListener('storage', onStorage)
  }, [isBff])

  useEffect(() => {
    if (isDemoModeEnabled() && isDemoSessionActive()) {
      setUser(getDemoUser())
      setToken(null)
      setAuthStatus('authenticated')
      return undefined
    }
    if (isBff) {
      void restoreBffSession()
      return undefined
    }
    if (!token) return undefined

    let cancelled = false
    ;(async () => {
      try {
        const response = await fetch(legacyUrl('/me'), {
          headers: { Authorization: `Bearer ${token}` },
        })
        if (!response.ok) {
          if (response.status === 401 || response.status === 403) await logout()
          return
        }
        const normalizedUser = normalizeUser(await response.json())
        if (cancelled) return
        setUser(normalizedUser)
        try {
          localStorage.setItem('authUser', JSON.stringify(normalizedUser))
        } catch {
          /* storage can be unavailable */
        }
      } catch (error) {
        if (!cancelled) console.error('[auth] /me error', error)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [isBff, logout, restoreBffSession, token])

  const value = useMemo(
    () => ({
      authMode: AUTH_MODE,
      authStatus,
      isLoading: isBff && authStatus === 'checking',
      accountLinked: isBff ? authStatus === 'authenticated' : Boolean(user),
      token,
      user,
      loggedIn:
        (isBff ? authStatus === 'authenticated' && Boolean(user) : Boolean(token && user)) ||
        (isDemoModeEnabled() && isDemoSessionActive()),
      login,
      loginWithAuth0,
      logout,
      restoreSession: restoreBffSession,
    }),
    [authStatus, isBff, login, loginWithAuth0, logout, restoreBffSession, token, user]
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export default AuthProvider
