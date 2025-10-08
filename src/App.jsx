import { BrowserRouter as Router, Routes, Route } from 'react-router-dom'
import Header from './components/Header'
import { Hero } from './components/Hero'
import { Footer } from './components/Footer'

function App() {
  return (
    <Router basename="/ai-case-learning-assistant">
      <div className="min-h-screen bg-gray-50">
        <Header />
        
        <main>
          <Routes>
            <Route path="/" element={
              <div className="max-w-7xl mx-auto px-6 py-12">
                <div className="text-center">
                  <Hero />
                </div>
              </div>
            } />
            <Route path="/login" element={
              <div className="max-w-md mx-auto mt-12 p-6 bg-white rounded-lg shadow-lg">
                <h2 className="text-2xl font-bold text-center mb-6">Sign In</h2>
                <p className="text-center text-gray-600">Login functionality coming soon!</p>
              </div>
            } />
            <Route path="/about" element={
              <div className="max-w-4xl mx-auto px-6 py-12">
                <h1 className="text-3xl font-bold text-gray-900 mb-6">About</h1>
                <p className="text-gray-600">Learn more about our AI Case Learning Assistant.</p>
              </div>
            } />
            <Route path="/privacy" element={
              <div className="max-w-4xl mx-auto px-6 py-12">
                <h1 className="text-3xl font-bold text-gray-900 mb-6">Privacy Policy</h1>
                <p className="text-gray-600">Your privacy is important to us.</p>
              </div>
            } />
            <Route path="/contact" element={
              <div className="max-w-4xl mx-auto px-6 py-12">
                <h1 className="text-3xl font-bold text-gray-900 mb-6">Contact Us</h1>
                <p className="text-gray-600">Get in touch with our team.</p>
              </div>
            } />
          </Routes>
        </main>
        <Footer />
      </div>
    </Router>
  )
}

export default App
