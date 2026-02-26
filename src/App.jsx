import React, { Suspense, lazy } from 'react'
import { Routes, Route, useLocation } from 'react-router-dom'
import Header from './components/Header'
import DeployConfigWarning from './components/DeployConfigWarning'
import { Hero } from './components/Hero'
import { Footer } from './components/Footer'
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
      <Toaster position="top-right" />
      <DeployConfigWarning />
      {!hideChrome && <Header />}
      <Suspense fallback={<div className="p-8 text-center">Loading…</div>}>
        <Routes>
          {/* Public / marketing pages WITH Header */}
          <Route
            path="/"
            element={
              <>
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
