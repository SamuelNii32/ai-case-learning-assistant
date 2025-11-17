import { useState, useEffect, useRef } from 'react'
import { Link, useNavigate, useLocation } from 'react-router-dom'
import toast from 'react-hot-toast'
import { Button } from '../components/ui/button'
import { Input } from '../components/ui/input'
import { Label } from '../components/ui/label'
import { FileText } from 'lucide-react'
import AuthCard from '../components/auth/AuthCard'
import RoleSelector from '../components/auth/RoleSelector'
import AuthFormFooter from '../components/auth/AuthFormFooter'
import { API_BASE } from '@/config'
import { useContext } from 'react'
import { AuthContext } from '@/contexts/AuthContext'

export default function SignInPage() {
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState('student')
  const [rememberMe, setRememberMe] = useState(false)
  const [loading, setLoading] = useState(false)
  const [err, setErr] = useState(null)
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
        console.log('j.data?.isSuperUser:', j?.data?.isSuperUser)
        console.log('j.user?.isSuperUser:', j?.user?.isSuperUser)
        console.log('j.user (raw):', j?.user)
        console.log('j.data (raw):', j?.data)

        const tokenCandidate =
          j?.token || j?.accessToken || j?.access_token || j?.jwt || j?.authToken || (j.data && (j.data.token || j.data.accessToken))
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

      // Make sure we carry over isSuperUser from the backend (if present)
      if (user) {
        user = {
          ...user,
          // pick isSuperUser from common shapes; fall back to existing value if already present
          isSuperUser: !!(j.isSuperUser ?? (j.data && j.data.isSuperUser) ?? user.isSuperUser),
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

      if (role === 'instructor') navigate('/instructor-dashboard')
      else navigate('/dashboard')
    } catch (e) {
      console.error('Login error', e)
      setErr(String(e.message || e))
    } finally {
      setLoading(false)
    }
  }

  return (
    <AuthCard
      className="bg-white"
      title={
        <>
          <div className="w-12 h-12 bg-[#125691] rounded-xl flex items-center justify-center">
            <FileText className="w-7 h-7 text-white" />
          </div>
          <h1 className="text-2xl font-bold text-gray-900">Welcome Back</h1>
        </>
      }
      description="Sign in to continue your case learning"
    >
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
          <Input
            id="password"
            type="password"
            placeholder="••••••••"
            value={password}
            onChange={e => setPassword(e.target.value)}
            required
          />
        </div>

        <RoleSelector role={role} onRoleChange={setRole} />

        <div className="flex items-center space-x-2">
          <input
            id="remember"
            type="checkbox"
            checked={rememberMe}
            onChange={e => setRememberMe(e.target.checked)}
            className="h-4 w-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
          />
          <Label htmlFor="remember" className="text-sm font-normal cursor-pointer">
            Remember me
          </Label>
        </div>

        <Button type="submit" className="w-full bg-[#125691] hover:bg-[#0f4f74]" disabled={loading}>
          {loading ? 'Signing in…' : 'Sign In'}
        </Button>

        {err && <div className="text-sm text-red-600 mt-2">{err}</div>}
      </form>

      <div className="text-center space-y-2">
        <Link to="/forgot-password" className="text-sm text-blue-600 hover:underline">
          Forgot your password?
        </Link>
      </div>

      <AuthFormFooter mode="signin" />
    </AuthCard>
  )
}
