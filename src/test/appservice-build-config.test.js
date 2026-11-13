import { describe, expect, it } from 'vitest'
import { validateAppServiceBuild } from '../../scripts/appservice-build-config.js'

describe('combined Azure App Service build configuration', () => {
  it('accepts an explicit BFF build with relative API URLs', () => {
    expect(() =>
      validateAppServiceBuild({
        command: 'build',
        mode: 'appservice',
        env: { VITE_AUTH_MODE: 'bff', VITE_API_BASE: '' },
      }),
    ).not.toThrow()
  })

  it('rejects a combined build that does not explicitly use BFF mode', () => {
    expect(() =>
      validateAppServiceBuild({ command: 'build', mode: 'appservice', env: {} }),
    ).toThrow(/VITE_AUTH_MODE=bff/)
  })

  it('rejects a cross-origin API base in a combined BFF build', () => {
    expect(() =>
      validateAppServiceBuild({
        command: 'build',
        mode: 'appservice',
        env: {
          VITE_AUTH_MODE: 'bff',
          VITE_API_BASE: 'https://api.example.test',
        },
      }),
    ).toThrow(/must not set VITE_API_BASE/)
  })

  it('does not change legacy or development builds', () => {
    expect(() =>
      validateAppServiceBuild({
        command: 'build',
        mode: 'production',
        env: { VITE_AUTH_MODE: 'jwt', VITE_API_BASE: 'https://api.example.test' },
      }),
    ).not.toThrow()
  })
})
