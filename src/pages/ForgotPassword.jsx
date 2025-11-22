import React, { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import toast from 'react-hot-toast'
import { Button } from '../components/ui/button'
import { Input } from '../components/ui/input'
import { Label } from '../components/ui/label'
import { FileText } from 'lucide-react'
import AuthCard from '../components/auth/AuthCard'
import AuthFormFooter from '../components/auth/AuthFormFooter'
import { API_BASE } from '@/config'

export default function ForgotPassword() {
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [loading, setLoading] = useState(false)
  const [msg, setMsg] = useState(null)
  const [err, setErr] = useState(null)

  const handleSubmit = async e => {
    e.preventDefault()
    setErr(null)
    setMsg(null)
    setLoading(true)
    const base = API_BASE ? String(API_BASE).replace(/\/$/, '') : ''
    // Try common forgot endpoints; backend may implement one of these
    const candidates = [`${base}/auth/forgot-password`, `${base}/auth/forgot`, `${base}/auth/password/forgot`]
    try {
      let lastErr = null
      for (const url of candidates) {
        try {
          const res = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ email }),
          })
          if (res.ok) {
            setMsg('If an account exists for that email, you will receive password reset instructions shortly.')
            toast.success('If an account exists, check your email for reset instructions.')
            return
          }
          lastErr = `Status ${res.status}`
        } catch (e) {
          lastErr = String(e)
        }
      }
      throw new Error(lastErr || 'Failed to request password reset')
    } catch (e) {
      console.error('Forgot password error', e)
      setErr('Failed to request password reset. Please try again later.')
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
          <h1 className="text-2xl font-bold text-gray-900">Reset your password</h1>
        </>
      }
      description="Enter the email for your account and we'll send reset instructions"
    >
      <form onSubmit={handleSubmit} className="space-y-4">
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

        <Button type="submit" className="w-full bg-[#125691] hover:bg-[#0f4f74]" disabled={loading}>
          {loading ? 'Sending…' : 'Send reset instructions'}
        </Button>

        {msg && <div className="text-sm text-green-600 mt-2">{msg}</div>}
        {err && <div className="text-sm text-red-600 mt-2">{err}</div>}
      </form>

      <div className="text-center pt-4">
        <Button variant="ghost" size="sm" onClick={() => navigate('/signin')}>
          Back to sign in
        </Button>
      </div>

      <AuthFormFooter mode="signin" />
    </AuthCard>
  )
}
