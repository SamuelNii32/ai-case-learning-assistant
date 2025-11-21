// src/config.js

// Try both env names so it's flexible:
// - VITE_API_BASE       (old style)
// - VITE_API_BASE_URL   (current / recommended)
const envBase = import.meta.env.VITE_API_BASE || import.meta.env.VITE_API_BASE_URL || ''

// Normalize: remove trailing slash if present
export const API_BASE = envBase ? envBase.replace(/\/$/, '') : ''
