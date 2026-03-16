import { Link } from 'react-router-dom'

const AuthFormFooter = ({ mode, className = '' }) => {
  if (mode === 'signin') {
    return (
      <div className={`text-center ${className}`}>
        <span className="text-sm text-[#5c4c3c]">
          Don't have an account?{' '}
          <Link to="/signup" className="font-medium text-[#C96A08] hover:text-[#9c5306]">
            Sign up
          </Link>
        </span>
      </div>
    )
  }

  if (mode === 'signup') {
    return (
      <div className={`text-center ${className}`}>
        <span className="text-sm text-[#5c4c3c]">
          Already have an account?{' '}
          <Link to="/signin" className="font-medium text-[#C96A08] hover:text-[#9c5306]">
            Sign in
          </Link>
        </span>
      </div>
    )
  }

  return null
}

export default AuthFormFooter
