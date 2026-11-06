import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const backend = env.STO123_BACKEND_URL || 'https://localhost:7232'
  const publicHost = env.STO123_PUBLIC_URL ? new URL(env.STO123_PUBLIC_URL).hostname : null
  return {
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    host: '127.0.0.1',
    allowedHosts: publicHost ? [publicHost] : [],
    proxy: {
      '/api': {
        target: backend,
        changeOrigin: true,
        secure: false,
      },
      '/hubs': {
        target: backend,
        changeOrigin: true,
        secure: false,
        ws: true,
      },
    },
  },
  }
})
