import React, { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button } from '../components/ui/button'
import { Input } from '../components/ui/input'
import { Label } from '../components/ui/label'
import { FileText } from 'lucide-react'
import AuthCard from '../components/auth/AuthCard'
import RoleSelector from '../components/auth/RoleSelector'
import AuthFormFooter from '../components/auth/AuthFormFooter'

export default function SignUpPage() {
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState('student')

  const handleSignUp = e => {
    e.preventDefault()
    localStorage.setItem('userRole', role)

    if (role === 'instructor') {
      navigate('/instructor-dashboard')
    } else {
      navigate('/dashboard')
    }
  }

  return (
    <AuthCard
      className="bg-gradient-to-br from-blue-50 via-indigo-50 to-purple-50"
      title={
        <>
          <div className="w-12 h-12 bg-gradient-to-r from-blue-600 to-indigo-600 rounded-xl flex items-center justify-center">
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

        <Button
          type="submit"
          className="w-full bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-700 hover:to-indigo-700"
        >
          Create Account
        </Button>
      </form>

      <AuthFormFooter mode="signup" />
    </AuthCard>
  )
}
