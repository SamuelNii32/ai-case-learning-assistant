import React, { useContext, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { AuthContext } from '@/contexts/AuthContext'

export default function RequireAuth({ children, requireInstructor = false }) {
  const auth = useContext(AuthContext)
  const navigate = useNavigate()
  const [checking, setChecking] = useState(true)

  useEffect(() => {
    // If no token at all, redirect immediately
    if (!auth?.token) {
      navigate('/login', { replace: true })
      return
    }

    // If token exists but user not yet loaded, wait until auth.user is set or token is cleared.
    if (auth.token && !auth.user) {
      setChecking(true)
      return
    }

    setChecking(false)
  }, [auth?.token, auth?.user, navigate])

  useEffect(() => {
    // If auth resolved and user not present (token was invalid), ensure redirect
    if (auth?.token && !auth?.user && !checking) {
      // token was present but user not loaded -> force login
      navigate('/login', { replace: true })
    }
  }, [auth?.token, auth?.user, checking, navigate])

  if (checking) {
    return (
      <div className="h-full flex items-center justify-center">
        <div className="text-sm text-muted-foreground">Checking authentication…</div>
      </div>
    )
  }

  // Role-based guard (optional) - require instructor role for some pages
  if (requireInstructor && auth?.user) {
    if (auth.user.role !== 'instructor') {
      // student tried to access instructor-only page
      navigate('/dashboard', { replace: true })
      return null
    }
  }

  if (!requireInstructor && auth?.user) {
    // If we want to protect student-only pages from instructors, redirect instructors
    // (optional; by default we allow instructors to view student pages)
  }

  return children
}
