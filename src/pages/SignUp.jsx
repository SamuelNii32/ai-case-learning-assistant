import React, { useState } from 'react'
import { Eye, EyeOff } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { Button } from '../components/ui/button'
import { Input } from '../components/ui/input'
import { Label } from '../components/ui/label'
import { FileText } from 'lucide-react'
import RoleSelector from '../components/auth/RoleSelector'
import AuthFormFooter from '../components/auth/AuthFormFooter'
import { API_BASE } from '@/config'

export default function SignUpPage() {
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [role, setRole] = useState('student')
  const [loading, setLoading] = useState(false)
  const [err, setErr] = useState(null)

  const handleSignUp = async e => {
    e.preventDefault()
    setErr(null)
    setLoading(true)
    const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
    const url = base ? `${base}/auth/signup` : '/auth/signup'
    try {
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          fullName: name,
          email,
          password,
          isInstructor: role === 'instructor',
        }),
      })
      if (!res.ok) {
        const txt = await res.text().catch(() => '')
        throw new Error(txt || `Signup failed (${res.status})`)
      }
      const j = await res.json()
      const user = {
        userId: j.userId || j.id,
        email: j.email || j.emailAddress,
        fullName: j.fullName || j.name,
      }
      // signup typically returns user info but not token per backend spec; persist user so SignIn/Auth can show context
      try {
        localStorage.setItem('authUser', JSON.stringify(user))
      } catch {
        try {
          localStorage.setItem('user', JSON.stringify(user))
        } catch {
          /* ignore */
        }
      }
      localStorage.setItem('userRole', role)
      // auto-redirect to login page with a success flag so the sign-in page can show confirmation
      navigate('/signin', { state: { signupSuccess: true } })
    } catch (e) {
      console.error('Signup error', e)
      setErr(String(e.message || e))
    } finally {
      setLoading(false)
    }
  }

  return (
    <main className="min-h-screen bg-[#f5ecde] flex items-stretch">
      <div className="w-full grid min-h-screen grid-cols-1 md:grid-cols-2">
        <section className="flex flex-col justify-center bg-[#f5ecde] px-8 py-12 text-[#2C2218]">
          <p className="text-xs uppercase tracking-[0.4em] text-[#3c2a1e] font-semibold">AI Case Assistant</p>
          <h2 className="mt-3 text-3xl font-semibold">Guided Case Learning</h2>
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
          <div className="w-full max-w-md space-y-6">
            <div className="flex flex-col items-center gap-2">
              <div className="w-12 h-12 rounded-xl bg-[#C96A08] flex items-center justify-center">
                <FileText className="w-7 h-7 text-white" />
              </div>
              <h1 className="text-2xl font-semibold text-[#2C2218]">Create Account</h1>
              <p className="text-center text-sm text-[#5c4c3c]">Start your case learning journey today</p>
            </div>

            <form onSubmit={handleSignUp} className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="name">Full Name</Label>
                <Input
                  id="name"
                  type="text"
                  placeholder="John Doe"
                  value={name}
                  onChange={e => setName(e.target.value)}
                  required
                />
              </div>

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

              <Button
                type="submit"
                className="w-full bg-[#C96A08] hover:bg-[#9c5306]"
                disabled={loading}
              >
                {loading ? 'Creating…' : 'Create Account'}
              </Button>

              {err && <div className="text-sm text-red-600 mt-2">{err}</div>}

              <AuthFormFooter mode="signup" />
            </form>
          </div>
        </section>
      </div>
    </main>
  )
}
