import { URL, fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'
import viteReact from '@vitejs/plugin-react'
import { tanstackRouter } from '@tanstack/router-plugin/vite'

// https://vitejs.dev/config/
export default defineConfig({
  cacheDir: '../../../.cache/vite/minecraft',
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
    port: 5174,
    host: '0.0.0.0', // Allow access from code-server proxy
    allowedHosts: ['code-server.pluscosmic.dev'],
    proxy: {
      '/api': {
        target: 'http://localhost:5261',
        changeOrigin: true,
      },
      '/auth': {
        target: 'http://localhost:5261',
        changeOrigin: true,
      },
    },
  },
})
