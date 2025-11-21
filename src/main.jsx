import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App.jsx'
import './index.css'
import AuthProvider from '@/contexts/AuthContext'

if (import.meta.env.DEV) {
  import('./utils/devFetchSniffer')
}

// Use Vite base if set (good when the app is hosted under a subpath).
const routerBase = import.meta.env.BASE_URL || '/'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <BrowserRouter basename={routerBase}>
      <AuthProvider>
        <App />
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>
)
