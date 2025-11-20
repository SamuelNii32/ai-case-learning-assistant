/// <reference types="vitest" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

// https://vite.dev/config/
export default defineConfig(() => ({
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
      // Dev-only: proxy uploads to backend to avoid CORS during local development
      '/uploads': {
        target: 'http://localhost:5259',
        changeOrigin: true,
        secure: false,
      },
      '/api': { target: 'http://localhost:5259', changeOrigin: true },
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
}))
