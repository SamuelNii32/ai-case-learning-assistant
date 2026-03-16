import { Link } from 'react-router-dom'
import { Button } from './ui/button'

export function Hero() {
  return (
    <div className="mx-auto max-w-2xl flex flex-col items-center text-center space-y-6 py-12">
      <h1 className="text-4xl sm:text-5xl font-bold text-[#2c2218] leading-tight">
        TEST HERO CHANGE
        <span className="block text-[#f97316] font-semibold italic mt-2">
          With AI Powered Learning
        </span>
      </h1>
      <p className="text-lg text-[#4a3b2c] leading-relaxed px-4 sm:px-0">
        Upload a case, choose a structured walkthrough or free Q&amp;A, and get evidence-grounded
        answers with citations that link every insight back to the source material.
      </p>
      <Link to="login">
        <Button
          size="lg"
          className="bg-[#f97316] text-white rounded-full px-10 py-3 shadow-none"
        >
          Sign In
        </Button>
      </Link>
    </div>
  )
}
