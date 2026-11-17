import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Same-origin /api in dev and in Docker (nginx), so the API needs no CORS.
  server: { proxy: { '/api': 'http://localhost:5080' } },
})
