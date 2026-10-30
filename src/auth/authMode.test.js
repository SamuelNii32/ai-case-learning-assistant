import { describe, expect, it } from 'vitest'
import { resolveAuthMode } from './authMode'

describe('resolveAuthMode', () => {
  it('enables BFF mode only when explicitly enabled in development', () => {
    expect(resolveAuthMode({ isDevelopment: true, bffEnabled: 'true' })).toBe('bff')
    expect(resolveAuthMode({ isDevelopment: true, bffEnabled: 'false' })).toBe('jwt')
    expect(resolveAuthMode({ isDevelopment: true, bffEnabled: undefined })).toBe('jwt')
  })

  it('requires the explicit mode for BFF in a production build', () => {
    expect(resolveAuthMode({ isDevelopment: false, bffEnabled: 'true' })).toBe('jwt')
    expect(resolveAuthMode({ isDevelopment: false, configuredMode: 'bff' })).toBe('bff')
    expect(resolveAuthMode({ isDevelopment: true, bffEnabled: 'true', configuredMode: 'jwt' })).toBe(
      'jwt'
    )
  })
})
