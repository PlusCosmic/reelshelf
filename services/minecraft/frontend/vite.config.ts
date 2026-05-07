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
    alias: [
      {
        find: '@',
        replacement: fileURLToPath(new URL('./src', import.meta.url)),
      },
      {
        find: /^@repo\/shared\/(.+)$/,
        replacement: `${fileURLToPath(new URL('../../../packages/shared/src', import.meta.url))}/$1`,
      },
      {
        find: '@repo/shared',
        replacement: fileURLToPath(new URL('../../../packages/shared/src/index.ts', import.meta.url)),
      },
      {
        find: '@repo/ui',
        replacement: fileURLToPath(new URL('../../../packages/ui/index.ts', import.meta.url)),
      },
    ],
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
