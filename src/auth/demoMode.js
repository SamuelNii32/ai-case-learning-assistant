// Demo Mode helper utilities
// Controls whether demo mode is enabled via env and manages a local demo session.

export function isDemoModeEnabled() {
  try {
    return String(import.meta.env?.VITE_DEMO_MODE) === 'true'
  } catch {
    return false
  }
}

export function isDemoSessionActive() {
  try {
    return typeof window !== 'undefined' && localStorage.getItem('demo_session') === 'true'
  } catch {
    return false
  }
}

export function startDemoSession() {
  try {
    if (typeof window !== 'undefined') {
      localStorage.setItem('demo_session', 'true')
      const demoUser = {
        userId: 'demo-user',
        email: 'designer@demo.example',
        fullName: 'Demo User',
        role: 'instructor', // grant broad access in demo
      }
      localStorage.setItem('demo_user', JSON.stringify(demoUser))
    }
  } catch {
    /* ignore */
  }
}

export function endDemoSession() {
  try {
    if (typeof window !== 'undefined') {
      localStorage.removeItem('demo_session')
      localStorage.removeItem('demo_user')
    }
  } catch {
    /* ignore */
  }
}

export function getDemoUser() {
  try {
    if (typeof window === 'undefined') return null
    const raw = localStorage.getItem('demo_user')
    return raw
      ? JSON.parse(raw)
      : { userId: 'demo-user', email: '', fullName: 'Demo User', role: 'instructor' }
  } catch {
    return { userId: 'demo-user', email: '', fullName: 'Demo User', role: 'instructor' }
  }
}
