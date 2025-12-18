import fs from 'node:fs'
import path from 'node:path'
import { defineConfig, type PluginOption } from 'vite';
import viteReact from "@vitejs/plugin-react";

// https://vitejs.dev/config/
export default defineConfig({
  // Cast to sidestep dual-module-type mismatch between Vite types
  plugins: [viteReact() as unknown as PluginOption],
  server: {
    // Use local.pluscosmic.dev for same-site cookies with nucleus.pluscosmic.dev
    host: 'local.pluscosmic.dev',
    https: {
      key: fs.readFileSync(path.resolve(__dirname, './local.pluscosmic.dev-key.pem')),
      cert: fs.readFileSync(path.resolve(__dirname, './local.pluscosmic.dev.pem')),
    },
  },
});
