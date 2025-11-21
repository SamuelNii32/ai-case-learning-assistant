// src/config.js

// Prefer the single canonical env var:
//   VITE_API_BASE = "https://...azurewebsites.net"
const envBase = (import.meta.env.VITE_API_BASE || '').trim()

// In development, fall back to local backend if VITE_API_BASE is not set.
// In production, if it's missing, we leave it empty and the UI will show
// a clear config error instead of silently calling the frontend origin.
const rawBase = envBase || (import.meta.env.DEV ? 'http://localhost:5259' : '')

// Normalized base without trailing slash (if empty, stays empty)
export const API_BASE = rawBase.replace(/\/$/, '')
