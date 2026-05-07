import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// VITE_API_URL is set at build time for production (the Container App URL).
// In development, the proxy below forwards API calls to the local agent API.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/sessions': 'http://localhost:8000',
      '/health':   'http://localhost:8000',
    }
  }
})
