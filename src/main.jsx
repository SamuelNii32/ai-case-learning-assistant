/* eslint-disable react-refresh/only-export-components */
import React from 'react'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, useNavigate } from 'react-router-dom'
import App from './App.jsx'
import './index.css'
import AuthProvider from '@/contexts/AuthContext'
import { setNavigator } from '@/lib/navigate'

if (import.meta.env.DEV) {
  import('./utils/devFetchSniffer')
}

// Use Vite base if set (good when the app is hosted under a subpath).
const routerBase = import.meta.env.BASE_URL || '/'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <BrowserRouter basename={routerBase}>
      {/* Register navigate so non-React modules can perform SPA navigation */}
      <NavigatorSetter />
      <AuthProvider>
        <App />
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>
)

function NavigatorSetter() {
  // This component registers the router navigate function for use by api/Auth modules.
  const navigate = useNavigate()
  React.useEffect(() => {
    setNavigator(navigate)
    return () => setNavigator(null)
  }, [navigate])
  return null
}
