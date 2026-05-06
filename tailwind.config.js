/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
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
        serif: ["Fraunces", "Source Serif 4", 'Georgia', 'serif'],
      },
      transitionProperty: {
        // allow using 'transition-width' via Tailwind if desired
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
