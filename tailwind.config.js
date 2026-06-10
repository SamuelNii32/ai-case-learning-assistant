/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        brand: {
          DEFAULT: '#C96A08',
          hover: '#9C5306',
          soft: '#FFF2E4',
        },
        warm: {
          background: '#FAF6F0',
          surface: '#F8F5EF',
          border: '#E4D6C7',
          text: '#2C2218',
          muted: '#5C4C3C',
        },
      },
      fontFamily: {
        sans: [
          'Montserrat',
          'system-ui',
          '-apple-system',
          'BlinkMacSystemFont',
          'Segoe UI',
          'Roboto',
          'Helvetica Neue',
          'Arial',
          'Noto Sans',
          'sans-serif',
        ],
        serif: ['Fraunces', 'Source Serif 4', 'Georgia', 'serif'],
      },
      transitionProperty: {
        width: 'width',
      },
      screens: {
        // Mobile: 0 - 768px (no prefix)
        // Tablet: 769px - 1240px
        // Desktop: 1241px - 1400px
        // Map the common responsive prefixes to your requested breakpoints
        md: '769px',
        lg: '1241px',
        xl: '1401px',
      },
    },
  },
  plugins: [],
}
