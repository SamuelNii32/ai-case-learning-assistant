import React, { Suspense, lazy } from 'react'
import { Routes, Route, useLocation } from 'react-router-dom'
import Header from './components/Header'
import DeployConfigWarning from './components/DeployConfigWarning'
import { Footer } from './components/Footer'
import Landing from './pages/Landing'
const FeaturesPage = lazy(() => import('./pages/Features'))
const AboutPage = lazy(() => import('./pages/About'))
const SupportPage = lazy(() => import('./pages/Support'))
const PrivacyPage = lazy(() => import('./pages/Privacy'))
const ContactPage = lazy(() => import('./pages/Contact'))
const SignInPage = lazy(() => import('./pages/SignIn'))
const SignUpPage = lazy(() => import('./pages/SignUp'))
const Dashboard = lazy(() => import('./pages/Dashboard'))
const SettingsPage = lazy(() => import('./pages/Settings'))
const ForgotPassword = lazy(() => import('./pages/ForgotPassword'))
const Workspace = lazy(() => import('./pages/Workspace'))
const AppLayout = lazy(() => import('./components/layout/AppLayout'))
const FocusLayout = lazy(() => import('./components/layout/FocusLayout'))
const SessionHistory = lazy(() => import('./pages/SessionHistory'))
const Upload = lazy(() => import('./pages/Upload'))
const InstructorDashboard = lazy(() => import('./pages/InstructorDashboard'))
const AdminSessions = lazy(() => import('./pages/admin/Sessions'))
const AdminSessionDetail = lazy(() => import('./pages/admin/SessionDetail'))
const AdminClasses = lazy(() => import('./pages/admin/Classes'))
const AdminClassDetail = lazy(() => import('./pages/admin/ClassDetail'))
const StudentClasses = lazy(() => import('./pages/StudentClasses'))
import { Toaster } from 'react-hot-toast'
import RequireAuth from './components/RequireAuth'

// Demo flow removed per request — demo route and redirect no longer included

const toastOptions = {
  duration: 3500,
  style: {
    background: '#fdf4eb',
    color: '#2c2218',
    border: '1px solid #e4d6c7',
    borderRadius: '14px',
    boxShadow: '0 10px 25px rgba(32,20,8,0.08)',
  },
  iconTheme: {
    primary: '#C96A08',
    secondary: '#ffffff',
  },
  success: {
    iconTheme: { primary: '#2f7a3f', secondary: '#ffffff' },
    style: {
      background: '#eaf8ee',
      border: '1px solid #cfeadc',
      color: '#25432c',
    },
  },
  error: {
    iconTheme: { primary: '#c94444', secondary: '#ffffff' },
    style: {
      background: '#fde5e5',
      border: '1px solid #f2c6c6',
      color: '#8c1c1c',
    },
  },
  info: {
    iconTheme: { primary: '#c96a08', secondary: '#ffffff' },
    style: {
      background: '#fef6ec',
      border: '1px solid #f2ddd0',
      color: '#2c2218',
    },
  },
}

function App() {
  const location = useLocation()
  const hideChrome = [
    '/upload',
    '/dashboard',
    '/classes',
    '/settings',
    '/session-history',
    '/workspace',
    '/login',
    '/signin',
    '/signup',
    '/instructor-dashboard',
    '/admin',
  ].some(p => location.pathname.startsWith(p))
  return (
    <div className="min-h-screen bg-gray-50">
      <Toaster
        position="bottom-right"
        toastOptions={toastOptions}
        gutter={12}
        containerStyle={{ bottom: 20, right: 20 }}
        closeButton={({ toast }) => (
          <button
            className="h-7 w-7 rounded-full bg-white/60 text-sm font-bold text-[#7a5c3c] transition hover:text-[#2c2218] hover:bg-white"
            aria-label="Close toast"
            onClick={() => toast.dismiss(toast.id)}
          >
            ×
          </button>
        )}
      />
      <DeployConfigWarning />
      {!hideChrome && <Header />}
      <Suspense fallback={<div className="p-8 text-center">Loading…</div>}>
        <Routes>
          {/* Public / marketing pages WITH Header */}
          <Route
            path="/"
            element={
              <>
                <Landing />
                <Footer />
              </>
            }
          />
          <Route
            path="/login"
            element={
              <main>
                <SignInPage />
              </main>
            }
          />
          <Route
            path="/signin"
            element={
              <main>
                <SignInPage />
              </main>
            }
          />
          <Route
            path="/forgot-password"
            element={
              <main>
                <ForgotPassword />
              </main>
            }
          />
          <Route
            path="/signup"
            element={
              <main>
                <SignUpPage />
              </main>
            }
          />
          <Route path="/upload" element={<Upload />} />

          {/* Demo route removed */}

          {/* Instructor dashboard route moved into AppLayout (actual component) */}
          <Route
            path="/features"
            element={
              <>
                <main>
                  <FeaturesPage />
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
                  <AboutPage />
                </main>
                <Footer />
              </>
            }
          />
          <Route
            path="/support"
            element={
              <>
                <main>
                  <SupportPage />
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
                  <PrivacyPage />
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
                  <ContactPage />
                </main>
                <Footer />
              </>
            }
          />

          {/* Dashboard WITHOUT Header - only AppLayout */}
          <Route element={<AppLayout />}>
            <Route
              path="/dashboard"
              element={
                <RequireAuth>
                  <Dashboard />
                </RequireAuth>
              }
            />
            <Route
              path="/classes"
              element={
                <RequireAuth>
                  <StudentClasses />
                </RequireAuth>
              }
            />
            <Route
              path="/admin/*"
              element={
                <RequireAuth requireInstructor>
                  <InstructorDashboard />
                </RequireAuth>
              }
            >
              <Route path="classes" element={<AdminClasses />} />
              <Route path="classes/:classId" element={<AdminClassDetail />} />
              <Route path="sessions" element={<AdminSessions />} />
              <Route path="sessions/:sessionId" element={<AdminSessionDetail />} />
              <Route path="upload" element={<Upload />} />
            </Route>
            <Route
              path="/settings"
              element={
                <RequireAuth>
                  <SettingsPage />
                </RequireAuth>
              }
            />
            <Route
              path="/session-history"
              element={
                <RequireAuth>
                  <SessionHistory />
                </RequireAuth>
              }
            />
            <Route
              path="/sessions"
              element={
                <RequireAuth>
                  <SessionHistory />
                </RequireAuth>
              }
            />
            <Route
              path="/cases"
              element={
                <RequireAuth>
                  <Dashboard />
                </RequireAuth>
              }
            />
            <Route
              path="/profile"
              element={
                <RequireAuth>
                  <SettingsPage />
                </RequireAuth>
              }
            />
            <Route
              path="/account"
              element={
                <RequireAuth>
                  <SettingsPage />
                </RequireAuth>
              }
            />
          </Route>

          {/* Workspace WITHOUT Header - only FocusLayout */}
          <Route element={<FocusLayout />}>
            <Route
              path="/workspace/:uploadId"
              element={
                <RequireAuth>
                  <Workspace />
                </RequireAuth>
              }
            />
          </Route>

          {/* 404 */}
          <Route path="*" element={<div className="p-8">Not Found</div>} />
        </Routes>
      </Suspense>
    </div>
  )
}

export default App
