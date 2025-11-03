import { defineConfig, type PluginOption } from 'vite';
import viteReact from "@vitejs/plugin-react";

// https://vitejs.dev/config/
export default defineConfig({
  // Cast to sidestep dual-module-type mismatch between Vite types
  plugins: [viteReact() as unknown as PluginOption],
});
