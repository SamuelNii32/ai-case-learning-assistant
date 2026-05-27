import React from 'react'

export default class ErrorBoundary extends React.Component {
  constructor(props) {
    super(props)
    this.state = { hasError: false, error: null }
  }

  static getDerivedStateFromError(error) {
    return { hasError: true, error }
  }

  componentDidCatch(error, info) {
    console.error('ErrorBoundary caught error:', error, info)
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="p-6 md:p-8">
          <h2 className="text-xl font-bold text-[#c76008]">Something went wrong</h2>
          <pre className="mt-3 whitespace-pre-wrap text-sm text-[#7a5c3c]">{String(this.state.error)}</pre>
        </div>
      )
    }
    return this.props.children
  }
}
