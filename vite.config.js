/// <reference types="vitest" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

// https://vite.dev/config/
export default defineConfig(({ mode }) => ({
	plugins: [react()],
	resolve: {
		alias: {
			'@': path.resolve(__dirname, 'src'),
		},
	},
		// During development serve at root so localhost:5174/ works. For production builds
		// use the configured subpath where the app will be hosted.
		base: mode === 'development' ? '/' : '/ai-case-learning-assistant/',
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

