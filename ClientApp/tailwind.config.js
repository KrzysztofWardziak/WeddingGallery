/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./src/**/*.{html,ts}",
  ],
  theme: {
    extend: {
      colors: {
        'dusty-teal': '#e8f0f0',     // Very light teal/cyan
        'navy-blue': '#1a2e45',      // Dark navy blue
        'teal-primary': '#0d7a7a',   // Dark teal / Sea blue
        'sea-glass': '#80cdc1',      // Light blue-green
        'eucalyptus': '#708f75',     // Eucalyptus green
        'sage-green': '#9fb5a2',     // Sage green
        'brass': '#b89947',          // Brass / Dark gold
        'gold': '#d4af37'            // Gold
      },
      fontFamily: {
        'sans': ['Inter', 'system-ui', 'sans-serif'],
        'serif': ['Playfair Display', 'serif']
      }
    },
  },
  plugins: [],
}
