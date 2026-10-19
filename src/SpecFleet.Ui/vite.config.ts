import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // The workspace proxy serves the dev server under this host; Vite 8 blocks
    // unknown Host headers by default.
    allowedHosts: [
      'work-1-uaslekbjznepeoqv.prod-runtime.all-hands.dev',
      'work-2-uaslekbjznepeoqv.prod-runtime.all-hands.dev',
      'localhost',
    ],
  },
})
