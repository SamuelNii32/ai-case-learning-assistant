import React, { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button } from '../components/ui/button'
import { Input } from '../components/ui/input'
import { Label } from '../components/ui/label'
import { FileText } from 'lucide-react'
import AuthCard from '../components/auth/AuthCard'
import RoleSelector from '../components/auth/RoleSelector'
import AuthFormFooter from '../components/auth/AuthFormFooter'
import { API_BASE } from '@/config'

export default function SignUpPage() {
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
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
    <AuthCard
      className="bg-white"
      title={
        <>
          <div className="w-12 h-12 bg-[#125691] rounded-xl flex items-center justify-center">
            <FileText className="w-7 h-7 text-white" />
          </div>
          <h1 className="text-2xl font-bold text-gray-900">Create Account</h1>
        </>
      }
      description="Start your case learning journey today"
    >
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

        <Button type="submit" className="w-full bg-[#125691] hover:bg-[#0f4f74]" disabled={loading}>
          {loading ? 'Creating…' : 'Create Account'}
        </Button>
        {err && <div className="text-sm text-red-600 mt-2">{err}</div>}
      </form>

      <AuthFormFooter mode="signup" />
    </AuthCard>
  )
}
