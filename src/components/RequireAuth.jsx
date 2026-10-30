import React, { useContext, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { AuthContext } from '@/contexts/AuthContext'
import { isDemoModeEnabled, isDemoSessionActive } from '@/auth/demoMode'

export default function RequireAuth({ children, requireInstructor = false }) {
  const auth = useContext(AuthContext)
  const navigate = useNavigate()
  const checking = Boolean(
    auth?.isLoading || (auth?.authMode !== 'bff' && auth?.token && !auth?.user)
  )

  useEffect(() => {
    if (isDemoModeEnabled() && isDemoSessionActive()) {
      return
    }
    if (!checking && !auth?.loggedIn) {
      navigate('/login', { replace: true })
    }
  }, [auth?.loggedIn, checking, navigate])

  if (checking) {
    return (
      <div className="h-full flex items-center justify-center">
        <div className="text-sm text-muted-foreground">Checking authentication…</div>
      </div>
    )
  }

  if (!auth?.loggedIn && !(isDemoModeEnabled() && isDemoSessionActive())) return null

  // Role-based guard (optional) - require instructor role for some pages
  if (requireInstructor) {
    // In demo mode, allow access regardless of role
    if (!(isDemoModeEnabled() && isDemoSessionActive())) {
      if (auth?.user && auth.user.role !== 'instructor') {
        navigate('/dashboard', { replace: true })
        return null
      }
    }
  }

  if (!requireInstructor && auth?.user) {
    // If we want to protect student-only pages from instructors, redirect instructors
    // (optional; by default we allow instructors to view student pages)
  }

  return children
}
