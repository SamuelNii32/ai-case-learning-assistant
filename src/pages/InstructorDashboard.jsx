import { useContext, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { AuthContext } from '@/contexts/AuthContext'

export default function InstructorDashboard() {
  const auth = useContext(AuthContext)
  const navigate = useNavigate()

  useEffect(() => {
    // If user is a supervisor / superuser, send them to the supervisor admin view
    if (auth?.user?.isSuperUser) {
      navigate('/admin/sessions', { replace: true })
      return
    }

    // If not logged in, send to sign in
    if (!auth?.loggedIn) {
      navigate('/login', { replace: true })
    }
    // Otherwise, keep here — placeholder for instructor-specific UI
  }, [auth?.user?.isSuperUser, auth?.loggedIn])

  return null
}
