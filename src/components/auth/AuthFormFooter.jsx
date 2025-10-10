import { Link } from 'react-router-dom'

const AuthFormFooter = ({ mode, className = '' }) => {
  if (mode === 'signin') {
    return (
      <div className={`text-center ${className}`}>
        <span className="text-sm text-gray-600">
          Don't have an account?{' '}
          <Link to="/signup" className="font-medium text-blue-600 hover:text-blue-500">
            Sign up
          </Link>
        </span>
      </div>
    )
  }

  if (mode === 'signup') {
    return (
      <div className={`text-center ${className}`}>
        <span className="text-sm text-gray-600">
          Already have an account?{' '}
          <Link to="/signin" className="font-medium text-blue-600 hover:text-blue-500">
            Sign in
          </Link>
        </span>
      </div>
    )
  }

  return null
}

export default AuthFormFooter
