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
  const location = useLocation()
  const auth = useContext(AuthContext)
  const signupToastShown = useRef(false)

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState('student')
  const [rememberMe, setRememberMe] = useState(false)
  const [loading, setLoading] = useState(false)
  const [err, setErr] = useState(null)
  const [showPassword, setShowPassword] = useState(false)

  useEffect(() => {
    if (location?.state?.signupSuccess && !signupToastShown.current) {
      signupToastShown.current = true
      toast.success('Account created - please sign in.')
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
  }, [location])

  async function handleSignIn(e) {
    e.preventDefault()
    setErr(null)
    setLoading(true)

    const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
    const url = base ? `${base}/auth/login` : '/auth/login'

    try {
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password }),
      })

      if (!res.ok) {
        if (res.status === 401) {
          setErr('Invalid email or password. Please check your credentials and try again.')
        } else {
          setErr('Sign in failed. Please try again later.')
        }
        return
      }

      const j = await res.json()
      const token =
        j.token ||
        j.accessToken ||
        j.access_token ||
        j.jwt ||
        j.authToken ||
        (j.data && (j.data.token || j.data.accessToken))

      let user = null
      if (j.user) user = j.user
      else if (j.data && j.data.user) user = j.data.user
      else if (j.userInfo) user = j.userInfo
      else {
        user = {
          userId: j.userId || j.id || (j.data && j.data.userId),
          email: j.email || (j.data && j.data.email),
          fullName: j.fullName || j.name || (j.data && j.data.fullName),
        }
      }

      if (user) {
        user = {
          ...user,
          role: j.role ?? (j.isSuperUser ? 'instructor' : role),
        }
      }

      if (auth && typeof auth.login === 'function') {
        auth.login(token, user)
      } else {
        if (token) localStorage.setItem('authToken', token)
        if (role) localStorage.setItem('userRole', role)
        if (user) localStorage.setItem('authUser', JSON.stringify(user))
      }

      const isInstructor = (user && user.role === 'instructor') || role === 'instructor'
      navigate(isInstructor ? '/admin/sessions' : '/dashboard')
    } catch (e) {
      const msg = String(e?.message || e)
      if (msg.includes('Failed to fetch') || msg.includes('NetworkError')) {
        setErr(
          'Network error while contacting the API. Check your connection and try again.'
        )
      } else {
        setErr(msg)
      }
    } finally {
      setLoading(false)
    }
  }

  return (
    <main className="relative min-h-screen bg-[#f5ecde]">
      <Link
        to="/"
        className="absolute left-4 top-4 z-10 rounded-md px-3 py-2 text-sm font-semibold text-[#C96A08] transition hover:bg-white/50 sm:left-6 sm:top-6"
        aria-label="Back to landing"
      >
        Back
      </Link>

      <div className="grid min-h-screen w-full grid-cols-1 md:grid-cols-2">
        <section className="hidden flex-col justify-center bg-[#f5ecde] px-8 py-12 text-[#2C2218] md:flex">
          <p className="text-xs font-semibold uppercase tracking-[0.24em] text-[#3c2a1e]">
            CasePilot
          </p>
          <h2 className="mt-3 text-3xl font-semibold text-[#2C2218]">Guided Case Learning</h2>
          <p className="mt-4 max-w-md text-sm leading-6 text-[#5c4c3c]">
            Build confidence with every case. Keep notes, insights, guided reading, and
            evidence-grounded discussion in one workspace.
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
              Instructor-ready classroom progress tracking
            </li>
          </ul>
        </section>

        <section className="flex items-center justify-center bg-[#f8f5ef] px-5 py-16 md:border-l md:border-[#ecdccf] md:px-8 md:py-12">
          <div className="w-full max-w-md space-y-8">
            <div className="flex flex-col items-center gap-2">
              <img src="/fav.png" alt="CasePilot logo" className="h-12 w-12" />
              <h1 className="text-2xl font-semibold text-[#2C2218]">Welcome back</h1>
              <p className="text-center text-sm text-[#5c4c3c]">
                Sign in to continue with CasePilot.
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
                    placeholder="Password"
                    value={password}
                    onChange={e => setPassword(e.target.value)}
                    required
                  />
                  <button
                    type="button"
                    aria-label={showPassword ? 'Hide password' : 'Show password'}
                    onClick={() => setShowPassword(v => !v)}
                    className="absolute right-2 top-1/2 -translate-y-1/2 rounded p-1 text-slate-500 hover:bg-slate-100"
                  >
                    {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
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
                  className="h-4 w-4 rounded border-[#c1a28a] text-[#C96A08] accent-[#C96A08] focus:ring-[#C96A08]"
                />
                <Label htmlFor="remember" className="cursor-pointer text-sm font-normal">
                  Remember me
                </Label>
              </div>

              {err && (
                <div className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
                  {err}
                </div>
              )}

              <Button
                type="submit"
                className="h-12 w-full rounded-[10px] bg-[#C96A08] px-4 text-sm font-semibold text-white hover:bg-[#9c5306]"
                disabled={loading}
              >
                {loading ? 'Signing in...' : 'Sign in'}
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
                  Continue as demo
                </Button>
              )}

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
