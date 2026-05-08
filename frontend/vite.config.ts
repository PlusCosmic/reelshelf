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

// https://vitejs.dev/config/
export default defineConfig({
  cacheDir: process.env.VITE_CACHE_DIR ?? "../.cache/vite/reelshelf",
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
    proxy: {
      "/api": apiProxy,
      "/auth": apiProxy,
    },
  },
});
