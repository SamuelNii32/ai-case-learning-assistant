export function resolveAuthMode({ isDevelopment, bffEnabled, configuredMode }) {
  const explicitMode = String(configuredMode || '').trim().toLowerCase()
  if (explicitMode === 'bff') return 'bff'
  if (explicitMode === 'jwt') return 'jwt'
  return isDevelopment && String(bffEnabled).toLowerCase() === 'true' ? 'bff' : 'jwt'
}

export const AUTH_MODE = resolveAuthMode({
  isDevelopment: import.meta.env.DEV,
  bffEnabled: import.meta.env.VITE_AUTH0_BFF_ENABLED,
  configuredMode: import.meta.env.VITE_AUTH_MODE,
})

export function isBffAuthMode() {
  return AUTH_MODE === 'bff'
}
