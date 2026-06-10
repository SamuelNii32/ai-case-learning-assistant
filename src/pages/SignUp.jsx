import React, { useState } from 'react'
import { Eye, EyeOff } from 'lucide-react'
import { Link, useNavigate } from 'react-router-dom'
import { Button } from '../components/ui/button'
import { Input } from '../components/ui/input'
import { Label } from '../components/ui/label'
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
  const [instructorInviteCode, setInstructorInviteCode] = useState('')
  const [loading, setLoading] = useState(false)
  const [err, setErr] = useState(null)

  async function handleSignUp(e) {
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
          instructorInviteCode: role === 'instructor' ? instructorInviteCode.trim() : '',
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

      try {
        localStorage.setItem('authUser', JSON.stringify(user))
        localStorage.setItem('userRole', role)
      } catch {
        /* ignore */
      }

      navigate('/signin', { state: { signupSuccess: true } })
    } catch (e) {
      setErr(String(e?.message || e))
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
          <h2 className="mt-3 text-3xl font-semibold">Guided Case Learning</h2>
          <p className="mt-4 max-w-md text-sm leading-6 text-[#5c4c3c]">
            Create a workspace for evidence-grounded case discussion, guided reading,
            notes, and classroom progress tracking.
          </p>
          <ul className="mt-6 space-y-3 text-sm text-[#5c4c3c]">
            <li className="flex items-start gap-2">
              <span className="mt-1 h-2 w-2 rounded-full bg-[#C96A08]" />
              Students join classes with instructor-provided codes
            </li>
            <li className="flex items-start gap-2">
              <span className="mt-1 h-2 w-2 rounded-full bg-[#C96A08]" />
              Instructors manage classes, cases, and learner progress
            </li>
            <li className="flex items-start gap-2">
              <span className="mt-1 h-2 w-2 rounded-full bg-[#C96A08]" />
              Reading Coach helps learners work through assigned documents
            </li>
          </ul>
        </section>

        <section className="flex items-center justify-center bg-[#f8f5ef] px-5 py-16 md:border-l md:border-[#ecdccf] md:px-8 md:py-12">
          <div className="w-full max-w-md space-y-6">
            <div className="flex flex-col items-center gap-2">
              <img src="/fav.png" alt="CasePilot logo" className="h-12 w-12" />
              <h1 className="text-2xl font-semibold text-[#2C2218]">Create account</h1>
              <p className="text-center text-sm text-[#5c4c3c]">
                Start learning with CasePilot.
              </p>
            </div>

            <form onSubmit={handleSignUp} className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="name">Full name</Label>
                <Input
                  id="name"
                  type="text"
                  placeholder="Jane Doe"
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

              {role === 'instructor' && (
                <div className="space-y-2">
                  <Label htmlFor="instructorInviteCode">Instructor invite code</Label>
                  <Input
                    id="instructorInviteCode"
                    type="text"
                    placeholder="Provided by your program admin"
                    value={instructorInviteCode}
                    onChange={e => setInstructorInviteCode(e.target.value)}
                    required
                  />
                  <p className="text-xs text-[#7a6654]">
                    Instructor accounts require a private invite code. Students do not need one.
                  </p>
                </div>
              )}

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
                {loading ? 'Creating...' : 'Create account'}
              </Button>

              <AuthFormFooter mode="signup" />
            </form>
          </div>
        </section>
      </div>
    </main>
  )
}
