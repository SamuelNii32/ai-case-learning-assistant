import { Routes, Route, useLocation } from 'react-router-dom'
import Header from './components/Header'
import Landing from './pages/Landing'
import { Footer } from './components/Footer'
import SignInPage from './pages/SignIn'
import SignUpPage from './pages/SignUp'
import Dashboard from './pages/Dashboard'
import SettingsPage from './pages/Settings'
import Workspace from './pages/Workspace'
import AppLayout from './components/layout/AppLayout'
import FocusLayout from './components/layout/FocusLayout'
import SessionHistory from "./pages/SessionHistory";
import Upload from "./pages/Upload";


function App() {
  const location = useLocation();
  // Hide the public site chrome (Header/Footer) on certain app routes
  // The original code only hid chrome for `/upload`. Dashboard, workspace,
  // settings and session-history are wrapped in AppLayout/FocusLayout and
  // are intended to render without the public Header, so include them here.
  const hideChrome = /^\/(upload|dashboard|settings|session-history|workspace)/.test(
    location.pathname
  );
  return (
      <div className="min-h-screen bg-gray-50">
        {!hideChrome && <Header />}
        <Routes>
          {/* Public / marketing pages WITH Header */}
          <Route path="/" element={<Landing />} />
          <Route
            path="/login"
            element={
              <>
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
                <main>
                  <SignUpPage />
                </main>
                <Footer />
              </>
            }
          />
                    <Route path="/upload" element={<Upload />} />

          <Route
            path="/instructor-dashboard"
            element={
              <>
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
              element={<SessionHistory />}
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
  )
}

export default App
