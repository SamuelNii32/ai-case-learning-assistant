import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { Button } from '../components/ui/button'
import { Input } from '../components/ui/input'
import { Label } from '../components/ui/label'
import { FileText } from 'lucide-react'
import AuthCard from '../components/auth/AuthCard'
import RoleSelector from '../components/auth/RoleSelector'
import AuthFormFooter from '../components/auth/AuthFormFooter'

export default function SignInPage() {
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState('student')
  const [rememberMe, setRememberMe] = useState(false)

  const handleSignIn = e => {
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

        <Button
          type="submit"
          className="w-full bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-700 hover:to-indigo-700"
        >
          Sign In
        </Button>
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
