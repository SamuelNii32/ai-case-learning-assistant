import { BrowserRouter as Router, Routes, Route } from 'react-router-dom'
import Header from './components/Header'
import { Hero } from './components/Hero'
import { Footer } from './components/Footer'
import SignInPage from './pages/SignIn'
import SignUpPage from './pages/SignUp'
import Dashboard from './pages/Dashboard'
import SettingsPage from './pages/Settings'
import Workspace from './pages/Workspace'
import AppLayout from './components/layout/AppLayout'
import FocusLayout from './components/layout/FocusLayout'

function App() {
  return (
    <Router basename="/ai-case-learning-assistant">
      <div className="min-h-screen bg-gray-50">
        <Routes>
          {/* Public / marketing pages WITH Header */}
          <Route
            path="/"
            element={
              <>
                <Header />
                <main>
                  <div className="max-w-7xl mx-auto px-6 py-12">
                    <div className="text-center">
                      <Hero />
                    </div>
                  </div>
                </main>
                <Footer />
              </>
            }
          />
          <Route
            path="/login"
            element={
              <>
                <Header />
                <main>
                  <SignInPage />
                </main>
                <Footer />
              </>
            }
          />
          <Route
            path="/signin"
            element={
              <>
                <Header />
                <main>
                  <SignInPage />
                </main>
                <Footer />
              </>
            }
          />
          <Route
            path="/signup"
            element={
              <>
                <Header />
                <main>
                  <SignUpPage />
                </main>
                <Footer />
              </>
            }
          />
          <Route
            path="/instructor-dashboard"
            element={
              <>
                <Header />
                <main>
                  <div className="max-w-4xl mx-auto px-6 py-12">
                    <h1 className="text-3xl font-bold text-gray-900 mb-6">Instructor Dashboard</h1>
                    <p className="text-gray-600">Instructor dashboard coming soon...</p>
                  </div>
                </main>
                <Footer />
              </>
            }
          />
          <Route
            path="/about"
            element={
              <>
                <Header />
                <main>
                  <div className="max-w-4xl mx-auto px-6 py-12">
                    <h1 className="text-3xl font-bold text-gray-900 mb-6">About</h1>
                    <p className="text-gray-600">
                      Learn more about our AI Case Learning Assistant.
                    </p>
                  </div>
                </main>
                <Footer />
              </>
            }
          />
          <Route
            path="/privacy"
            element={
              <>
                <Header />
                <main>
                  <div className="max-w-4xl mx-auto px-6 py-12">
                    <h1 className="text-3xl font-bold text-gray-900 mb-6">Privacy Policy</h1>
                    <p className="text-gray-600">Your privacy is important to us.</p>
                  </div>
                </main>
                <Footer />
              </>
            }
          />
          <Route
            path="/contact"
            element={
              <>
                <Header />
                <main>
                  <div className="max-w-4xl mx-auto px-6 py-12">
                    <h1 className="text-3xl font-bold text-gray-900 mb-6">Contact Us</h1>
                    <p className="text-gray-600">Get in touch with our team.</p>
                  </div>
                </main>
                <Footer />
              </>
            }
          />

          {/* Dashboard WITHOUT Header - only AppLayout */}
          <Route element={<AppLayout />}>
            <Route path="/dashboard" element={<Dashboard />} />
            <Route path="/settings" element={<SettingsPage />} />
            <Route
              path="/session-history"
              element={
                <div className="p-8">
                  <h1 className="text-2xl font-bold">Session History</h1>
                  <p>Session history coming soon...</p>
                </div>
              }
            />
          </Route>

          {/* Workspace WITHOUT Header - only FocusLayout */}
          <Route element={<FocusLayout />}>
            <Route path="/workspace/:id" element={<Workspace />} />
          </Route>

          {/* 404 */}
          <Route path="*" element={<div className="p-8">Not Found</div>} />
        </Routes>
      </div>
    </Router>
  )
}

export default App
