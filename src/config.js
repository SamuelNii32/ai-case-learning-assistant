// src/config.js

// Prefer the single canonical env var set at build time:
//   VITE_API_BASE = "https://...azurewebsites.net"
// We also support a runtime override `window.__API_BASE__` that some
// deploy systems can inject into the page for greater flexibility.
const envBase = (import.meta.env.VITE_API_BASE || '').trim()
const runtimeBase = typeof window !== 'undefined' ? (window.__API_BASE__ || '').trim() : ''

// In development, fall back to local backend if VITE_API_BASE is not set.
// In production, if it's missing, we leave it empty and the UI can show
// a clear config error instead of silently calling the frontend origin.
const rawBase = envBase || runtimeBase || (import.meta.env.DEV ? 'http://localhost:5259' : '')

// Normalized base without trailing slash (if empty, stays empty)
export const API_BASE = rawBase.replace(/\/$/, '')

export function hasApiBase() {
	return Boolean(API_BASE && String(API_BASE).trim())
}
