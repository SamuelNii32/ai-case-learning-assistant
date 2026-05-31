import { useState, useEffect, useRef, useContext } from 'react'
import { Link, useNavigate, useLocation } from 'react-router-dom'
import toast from 'react-hot-toast'
import { Eye, EyeOff } from 'lucide-react'
import { Button } from '../components/ui/button'
import { Input } from '../components/ui/input'
import { Label } from '../components/ui/label'
import RoleSelector from '../components/auth/RoleSelector'
import AuthFormFooter from '../components/auth/AuthFormFooter'
import { API_BASE } from '@/config'
import { isDemoModeEnabled, startDemoSession } from '@/auth/demoMode'
import { AuthContext } from '@/contexts/AuthContext'

export default function SignInPage() {
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState('student')
  const [rememberMe, setRememberMe] = useState(false)
  const [loading, setLoading] = useState(false)
  const [err, setErr] = useState(null)
  const [showPassword, setShowPassword] = useState(false)
  const auth = useContext(AuthContext)
  const location = useLocation()
  const signupToastShown = useRef(false)

  useEffect(() => {
    try {
      if (location?.state?.signupSuccess && !signupToastShown.current) {
        signupToastShown.current = true
        toast.success('Account created — please sign in.')
        // clear the history state so the message doesn't persist on navigation
        try {
          window.history.replaceState(
            {},
            document.title,
            window.location.pathname + window.location.search
          )
        } catch {
          /* ignore */
        }
      }
    } catch {
      /* ignore */
    }
  }, [location])

  async function handleSignIn(e) {
    e.preventDefault()
    setErr(null)
    setLoading(true)
    const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
    const url = base ? `${base}/auth/login` : '/auth/login'
    // Developer debug: log the final URL used for the login request so
    // deployed builds can reveal misconfigured VITE_API_BASE or routing/CORS issues.
    try {
      console.log('Login POST URL:', url)
    } catch {
      /* ignore */
    }
    try {
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password }),
      })
      if (!res.ok) {
        const txt = await res.text().catch(() => '')
        // Log full details for developers
        console.error('Login failed response', { status: res.status, body: txt })
        // Show friendly messages for common cases
        if (res.status === 401) {
          setErr('Invalid email or password. Please check your credentials and try again.')
        } else {
          setErr('Sign in failed. Please try again later.')
        }
        setLoading(false)
        return
      }
      const j = await res.json()
      // -- DEBUG: inspect raw login response and token payload (remove after testing) --
      try {
        console.log('LOGIN RESPONSE JSON:', j)
        console.log('j.isSuperUser:', j?.isSuperUser)
        console.log('j.role:', j?.role)
        console.log('j.data?.isSuperUser:', j?.data?.isSuperUser)
        console.log('j.data?.role:', j?.data?.role)
        console.log('j.user?.isSuperUser:', j?.user?.isSuperUser)
        console.log('j.user?.role:', j?.user?.role)
        console.log('j.user (raw):', j?.user)
        console.log('j.data (raw):', j?.data)

        const tokenCandidate =
          j?.token ||
          j?.accessToken ||
          j?.access_token ||
          j?.jwt ||
          j?.authToken ||
          (j.data && (j.data.token || j.data.accessToken))
        console.log('token candidates:', {
          token: j?.token,
          accessToken: j?.accessToken,
          access_token: j?.access_token,
          jwt: j?.jwt,
          authToken: j?.authToken,
        })
        if (tokenCandidate) {
          try {
            const base64 = tokenCandidate.split('.')[1]
            const parsed = JSON.parse(decodeURIComponent(escape(atob(base64))))
            console.log('decoded JWT payload:', parsed)
            console.log('payload.isSuperUser:', parsed?.isSuperUser)
            console.log('payload.role:', parsed?.role)
          } catch (err) {
            console.warn('Failed to decode JWT payload', err)
          }
        }
      } catch (err) {
        console.warn('Debug logging failed', err)
      }
      // Accept multiple possible token names from different backends
      const token =
        j.token ||
        j.accessToken ||
        j.access_token ||
        j.jwt ||
        j.authToken ||
        (j.data && (j.data.token || j.data.accessToken))

      // Normalize user info from common shapes
      let user = null
      if (j.user) user = j.user
      else if (j.data && j.data.user) user = j.data.user
      else if (j.userInfo) user = j.userInfo
      else
        user = {
          userId: j.userId || j.id || (j.data && j.data.userId),
          email: j.email || (j.data && j.data.email),
          fullName: j.fullName || j.name || (j.data && j.data.fullName),
        }

      // Make sure we carry over an explicit role from the backend (if present)
      if (user) {
        user = {
          ...user,
          // Prefer the server-provided `role`. For backwards compatibility
          // fall back to `isSuperUser` when present, otherwise use the
          // selected role from the RoleSelector (`role` state).
          role: j.role ?? (j.isSuperUser ? 'instructor' : role),
        }
      }
      // update centralized auth state so the rest of the SPA knows we're logged in
      // Prefer calling the centralized login so AuthProvider updates SPA state
      if (auth && typeof auth.login === 'function') {
        auth.login(token, user)
      } else {
        // fallback: persist to localStorage so a reload will work
        if (token) localStorage.setItem('authToken', token)
        if (role) localStorage.setItem('userRole', role)
        // AuthContext listens for 'authUser' and 'user' keys; prefer authUser
        if (user) {
          try {
            localStorage.setItem('authUser', JSON.stringify(user))
          } catch {
            localStorage.setItem('user', JSON.stringify(user))
          }
        }
      }

      // Prefer server-provided role when deciding where to navigate.
      const isInstructor = (user && user.role === 'instructor') || role === 'instructor'
      // Instructors/supervisors should go to the supervisor admin view.
      if (isInstructor) navigate('/admin/sessions')
      else navigate('/dashboard')
    } catch (e) {
      // Provide a clearer, actionable message for common network/CORS issues
      console.error('Login error', { err: e, url })
      const msg = String(e?.message || e)
      if (msg.includes('Failed to fetch') || msg.includes('NetworkError')) {
        setErr('We could not sign you in right now. Please try again in a moment.')
      } else {
        setErr(msg)
      }
    } finally {
      setLoading(false)
    }
  }

  return (
    <main className="relative min-h-screen bg-[#f5ecde] flex items-stretch">
      <Link
        to="/"
        className="absolute left-6 top-6 text-[32px] font-semibold text-[#C96A08]"
        aria-label="Back to landing"
      >
        ←
      </Link>
      <div className="w-full grid min-h-screen grid-cols-1 md:grid-cols-2">
        <section className="flex flex-col justify-center bg-[#f5ecde] px-8 py-12 text-[#2C2218]">
          <p className="text-xs uppercase tracking-[0.4em] text-[#3c2a1e] font-semibold">
            AI Case Assistant
          </p>
          <h2 className="mt-3 text-3xl font-semibold text-[#2C2218]">Guided Case Learning</h2>
          <p className="mt-4 text-sm text-[#5c4c3c]">
            Build confidence with every case. Breakthroughs come faster when every note, insight,
            and walkthrough stays close at hand.
          </p>
          <ul className="mt-6 space-y-3 text-sm text-[#5c4c3c]">
            <li className="flex items-start gap-2">
              <span className="mt-1 h-2 w-2 rounded-full bg-[#C96A08]" />
              Guided case walkthroughs
            </li>
            <li className="flex items-start gap-2">
              <span className="mt-1 h-2 w-2 rounded-full bg-[#C96A08]" />
              Evidence-grounded insights and notes in one place
            </li>
            <li className="flex items-start gap-2">
              <span className="mt-1 h-2 w-2 rounded-full bg-[#C96A08]" />
              Private by design
            </li>
          </ul>
        </section>

        <section className="flex items-center justify-center bg-[#f8f5ef] px-8 py-12 md:border-l md:border-[#ecdccf]">
          <div className="w-full max-w-md space-y-8">
            <div className="flex flex-col items-center gap-2">
              <img src="/fav.png" alt="CasePilot logo" className="h-12 w-12" />
              <h1 className="text-2xl font-semibold text-[#2C2218]">Welcome Back</h1>
              <p className="text-center text-sm text-[#5c4c3c]">
                Sign in to continue your case learning
              </p>
            </div>

            <form onSubmit={handleSignIn} className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="email">Email</Label>
                <Input
                  id="email"
                  type="email"
                  placeholder="you@example.com"
                  value={email}
                  onChange={e => setEmail(e.target.value)}
                  required
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="password">Password</Label>
                <div className="relative">
                  <Input
                    id="password"
                    type={showPassword ? 'text' : 'password'}
                    placeholder="••••••••"
                    value={password}
                    onChange={e => setPassword(e.target.value)}
                    required
                  />
                  <button
                    type="button"
                    aria-label={showPassword ? 'Hide password' : 'Show password'}
                    onClick={() => setShowPassword(v => !v)}
                    className="absolute right-2 top-1/2 -translate-y-1/2 text-slate-500"
                    style={{ background: 'transparent', border: 'none' }}
                  >
                    {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                  </button>
                </div>
              </div>

              <RoleSelector role={role} onRoleChange={setRole} />

              <div className="flex items-center space-x-2">
                <input
                  id="remember"
                  type="checkbox"
                  checked={rememberMe}
                  onChange={e => setRememberMe(e.target.checked)}
                  className="h-4 w-4 rounded border-[#c1a28a] text-[#C96A08] focus:ring-[#C96A08] accent-[#C96A08] cursor-pointer"
                />
                <Label htmlFor="remember" className="text-sm font-normal cursor-pointer">
                  Remember me
                </Label>
              </div>

              <Button
                type="submit"
                className="w-full h-12 rounded-[10px] bg-[#C96A08] px-4 text-sm font-semibold text-white hover:bg-[#9c5306]"
                disabled={loading}
              >
                {loading ? 'Signing in…' : 'Sign In'}
              </Button>

              {isDemoModeEnabled() && (
                <Button
                  type="button"
                  variant="outline"
                  className="w-full"
                  onClick={() => {
                    startDemoSession()
                    navigate('/dashboard')
                  }}
                >
                  Continue as Demo
                </Button>
              )}

              {err && <div className="text-sm text-red-600 mt-2">{err}</div>}

              <div className="text-center">
                <Link
                  to="/forgot-password"
                  className="text-sm font-medium text-[#C96A08] hover:underline"
                >
                  Forgot your password?
                </Link>
              </div>

              <AuthFormFooter mode="signin" />
            </form>
          </div>
        </section>
      </div>
    </main>
  )
}
