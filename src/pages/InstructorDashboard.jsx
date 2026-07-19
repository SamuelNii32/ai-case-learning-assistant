import { useContext, useEffect } from 'react'
import { useNavigate, Outlet } from 'react-router-dom'
import { AuthContext } from '@/contexts/AuthContext'
import InstructorNav from '@/components/instructor/InstructorNav'

export default function InstructorDashboard() {
  const auth = useContext(AuthContext)
  const navigate = useNavigate()

  useEffect(() => {
    // If not logged in, send to sign in
    if (!auth?.loggedIn) {
      navigate('/login', { replace: true })
    }
  }, [auth?.loggedIn, navigate])

  // Redirect legacy instructor landing routes to the classes page.
  useEffect(() => {
    if (auth?.user?.role === 'instructor' && window.location.pathname === '/instructor-dashboard') {
      navigate('/admin/classes', { replace: true })
    }
  }, [auth?.user?.role, navigate])

  return (
    <div className="min-h-screen bg-white text-[#2c2218]">
      <InstructorNav />
      <Outlet />
    </div>
  )
}
