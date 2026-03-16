import { Card } from '../ui/card'

const AuthCard = ({ title, description, children, className = '' }) => {
  return (
    <div
      className={`min-h-screen flex items-center justify-center bg-[#f8f5ef] px-4 py-10 ${className}`}
    >
      <Card className="w-full max-w-sm sm:max-w-md bg-[#fffdf9] p-6 sm:p-8 space-y-5 shadow-[0_18px_35px_rgba(44,34,24,0.08)] border border-[#ecdccf]">
        <div className="flex flex-col items-center space-y-3">
          {title}
          {description && (
            <p className="text-[#5c4c3c] text-center text-sm sm:text-base">{description}</p>
          )}
        </div>
        {children}
      </Card>
    </div>
  )
}

export default AuthCard
