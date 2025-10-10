import { Card } from '../ui/card'

const AuthCard = ({ title, description, children, className = '' }) => {
  return (
    <div className={`min-h-screen flex items-center justify-center bg-gray-50 p-4 ${className}`}>
      <Card className="w-full max-w-sm sm:max-w-md p-6 sm:p-8 space-y-4 sm:space-y-6">
        <div className="flex flex-col items-center space-y-2">
          {title}
          {description && (
            <p className="text-gray-600 text-center text-sm sm:text-base">{description}</p>
          )}
        </div>
        {children}
      </Card>
    </div>
  )
}

export default AuthCard
