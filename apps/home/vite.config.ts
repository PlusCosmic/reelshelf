import fs from 'node:fs'
import path from 'node:path'
import { defineConfig, type PluginOption } from 'vite';
import viteReact from "@vitejs/plugin-react";

// Load HTTPS certs only if they exist (for local dev)
const keyPath = path.resolve(__dirname, './local.pluscosmic.dev-key.pem')
const certPath = path.resolve(__dirname, './local.pluscosmic.dev.pem')
const hasLocalCerts = fs.existsSync(keyPath) && fs.existsSync(certPath)

// https://vitejs.dev/config/
export default defineConfig({
  // Cast to sidestep dual-module-type mismatch between Vite types
  plugins: [viteReact() as unknown as PluginOption],
  server: {
    // Use local.pluscosmic.dev for same-site cookies with nucleus.pluscosmic.dev
    host: 'local.pluscosmic.dev',
    ...(hasLocalCerts && {
      https: {
        key: fs.readFileSync(keyPath),
        cert: fs.readFileSync(certPath),
      },
    }),
  },
});
