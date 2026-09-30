import { URL, fileURLToPath } from "node:url";
import { defineConfig, type ProxyOptions } from "vite";
import viteReact from "@vitejs/plugin-react";
import { tanstackRouter } from "@tanstack/router-plugin/vite";

const apiProxy: ProxyOptions = {
  target: "http://localhost:5260",
  changeOrigin: false,
  xfwd: true,
  configure(proxy) {
    proxy.on("proxyReq", (proxyReq, req) => {
      const forwardedProto = req.headers["x-forwarded-proto"];
      proxyReq.setHeader(
        "X-Forwarded-Proto",
        typeof forwardedProto === "string"
          ? forwardedProto.split(",")[0]
          : "https",
      );
    });
  },
};

const cacheDir = process.env.VITE_CACHE_DIR ?? "../.cache/vite/reelshelf";

// https://vitejs.dev/config/
export default defineConfig({
  cacheDir,
  plugins: [
    tanstackRouter({
      target: "react",
      autoCodeSplitting: true,
    }),
    viteReact(),
  ],
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  server: {
    // Vite's default allow-list is the whole workspace, so a public dev server would hand out anything
    // in the checkout (API config, Data Protection keys) under /@fs/. Serve only what the app loads.
    fs: {
      allow: [".", "../node_modules", cacheDir],
    },
    proxy: {
      "/api": apiProxy,
      "/auth": apiProxy,
    },
  },
});
