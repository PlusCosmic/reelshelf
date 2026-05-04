import { URL, fileURLToPath } from 'node:url'
import fs from 'node:fs'
import path from 'node:path'
import { defineConfig } from 'vite'
import viteReact from '@vitejs/plugin-react'
import { tanstackRouter } from '@tanstack/router-plugin/vite'

// Load HTTPS certs only if they exist (for local dev)
const keyPath = path.resolve(__dirname, './local.pluscosmic.dev-key.pem')
const certPath = path.resolve(__dirname, './local.pluscosmic.dev.pem')
const hasLocalCerts = fs.existsSync(keyPath) && fs.existsSync(certPath)

// https://vitejs.dev/config/
export default defineConfig({
  cacheDir: '../../../.cache/vite/clips',
  plugins: [
    tanstackRouter({
      target: 'react',
      autoCodeSplitting: true,
    }),
    viteReact(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    // Use local.pluscosmic.dev for same-site cookies
    host: 'local.pluscosmic.dev',
    ...(hasLocalCerts && {
      https: {
        key: fs.readFileSync(keyPath),
        cert: fs.readFileSync(certPath),
      },
    }),
    proxy: {
      '/api': {
        target: 'http://localhost:5260',
        changeOrigin: true,
      },
      '/auth': {
        target: 'http://localhost:5260',
        changeOrigin: true,
      },
    },
  },
})
