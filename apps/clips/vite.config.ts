import { URL, fileURLToPath } from 'node:url'
import fs from 'node:fs'
import path from 'node:path'
import { defineConfig } from 'vite'
import viteReact from '@vitejs/plugin-react'
import { tanstackRouter } from '@tanstack/router-plugin/vite'

// https://vitejs.dev/config/
export default defineConfig({
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
    // Use local.pluscosmic.dev for same-site cookies with nucleus.pluscosmic.dev
    host: 'local.pluscosmic.dev',
    https: {
      key: fs.readFileSync(path.resolve(__dirname, './local.pluscosmic.dev-key.pem')),
      cert: fs.readFileSync(path.resolve(__dirname, './local.pluscosmic.dev.pem')),
    },
  },
})
