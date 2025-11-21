import React from 'react'
import { hasApiBase } from '@/config'

export default function DeployConfigWarning() {
  // Show only in production when API base not set
  if (import.meta.env.DEV) return null
  if (hasApiBase()) return null

  // Render a prominent banner and log to console
  try {
    console.error('[deploy-config] VITE_API_BASE is not set. Set VITE_API_BASE in your host (e.g., Vercel) environment variables to the API base URL.')
  } catch {
    /* ignore */
  }

  return (
    <div className="bg-yellow-50 border-l-4 border-yellow-400 p-4">
      <div className="max-w-7xl mx-auto">
        <p className="text-sm text-yellow-700">
          Deployment config warning: <strong>VITE_API_BASE</strong> is not set. Sign-in and API calls may fail. Add <code>VITE_API_BASE</code> to your hosting environment variables (for Vercel set it in Project → Settings → Environment Variables) and redeploy.
        </p>
      </div>
    </div>
  )
}
