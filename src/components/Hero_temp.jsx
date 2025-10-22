import { Link } from 'react-router-dom'
import { Button } from './ui/button'

export function Hero() {
  return (
    <div className="space-y-8 bg-white p-8 rounded-lg shadow-lg">
      <h1 className="text-6xl font-bold text-gray-900 leading-tight">
        Analyze complex cases. Learn with AI.
      </h1>
      <p className="text-xl text-gray-600 leading-relaxed">
        Upload a case, choose guided walkthrough or free Q&A, and get evidence-grounded answers with
        citations.
      </p>
      <div className="flex items-center gap-4">
        <Link to="/login">
          <Button size="lg" className="bg-[#125691] hover:bg-[#0f4f74]">
            Sign In
          </Button>
        </Link>
        <Link to="/dashboard">
          <Button size="lg" variant="outline">
            Try Demo
          </Button>
        </Link>
      </div>
    </div>
  )
}
