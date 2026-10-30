/// <reference types="vitest" />
import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, __dirname, '')
  const bffTarget = env.VITE_BFF_PROXY_TARGET || 'https://localhost:7002'
  const bffProxy = {
    target: bffTarget,
    changeOrigin: true,
    secure: false,
  }

  return {
    plugins: [react()],
    resolve: {
      alias: {
        '@': path.resolve(__dirname, 'src'),
      },
    },
    // Use root base path for both development and production so the app
    // is portable when deployed to the site root (e.g., Vercel).
    // If you need to host under a subpath, set a custom value here or
    // use an environment-specific override.
    base: '/',
    server: {
      port: 5174,
      strictPort: true,
      proxy: {
        // Development-only same-origin BFF surface. The frontend API client still
        // blocks every route that has not been explicitly enabled server-side.
        '/bff': bffProxy,
        '/signin-auth0': bffProxy,
        '/signout-callback-auth0': bffProxy,
        '/me': bffProxy,
        '/uploads': bffProxy,
        '/sessions': bffProxy,
        '/classes': bffProxy,
        '/index': bffProxy,
        '/ask': bffProxy,
        '/search': bffProxy,
        '/tutor': bffProxy,
        '/cases': bffProxy,
        '/admin': bffProxy,
        '/api': bffProxy,
      },
    },
    test: {
      globals: true,
      environment: 'jsdom',
      setupFiles: './src/test/setup.js',
      coverage: {
        provider: 'v8',
        reporter: ['text', 'json', 'html', 'lcov'],
        exclude: [
          'node_modules/',
          'src/test/',
          '**/*.test.{js,jsx}',
          'vite.config.js',
          'tailwind.config.js',
        ],
      },
    },
  }
})
