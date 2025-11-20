import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App.jsx'
import './index.css'
import AuthProvider from '@/contexts/AuthContext'

// Dev-only fetch sniffer to help debug Authorization headers and 401s
if (import.meta.env.DEV) {
  import('./utils/devFetchSniffer')
}

// Use the configured BASE_URL in production but default to '/' during
// development so the dev server is reachable at http://localhost:5174/
const routerBase = import.meta.env.DEV ? '/' : import.meta.env.BASE_URL

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <BrowserRouter basename={routerBase}>
      <AuthProvider>
        <App />
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>
)
