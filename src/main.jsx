import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { HashRouter } from 'react-router-dom'
import App from './App.jsx'
import './index.css'
import AuthProvider from '@/contexts/AuthContext'

if (import.meta.env.DEV) {
  import('./utils/devFetchSniffer')
}

const routerBase = '/'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <HashRouter basename={routerBase}>
      <AuthProvider>
        <App />
      </AuthProvider>
    </HashRouter>
  </StrictMode>
)
