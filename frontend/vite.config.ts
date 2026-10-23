import { URL, fileURLToPath } from "node:url";
import { defineConfig, type Plugin, type ProxyOptions } from "vite";
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

// The fonts the front page's first screen is set in: the logo and headline in Fraunces (roman and
// italic) and the copy in Instrument Sans, Latin subset only. Preloading them lets them download
// alongside the app's JavaScript instead of after it has rendered.
const preloadedFonts = [
  /^assets\/fraunces-latin-full-normal-.+\.woff2$/,
  /^assets\/fraunces-latin-full-italic-.+\.woff2$/,
  /^assets\/instrument-sans-latin-wght-normal-.+\.woff2$/,
];

function preloadFonts(): Plugin {
  return {
    name: "reelshelf-preload-fonts",
    apply: "build",
    transformIndexHtml: {
      order: "post",
      handler(_html, { bundle }) {
        const files = Object.keys(bundle ?? {});
        return preloadedFonts.map((pattern) => {
          const file = files.find((name) => pattern.test(name));
          if (!file) {
            throw new Error(`No built font matches ${pattern}`);
          }
          return {
            tag: "link",
            attrs: {
              rel: "preload",
              href: `/${file}`,
              as: "font",
              type: "font/woff2",
              crossorigin: "",
            },
            injectTo: "head",
          };
        });
      },
    },
  };
}

// The router fetches the matched route's split component before it renders anything, so on a cold load
// the library layout (the page at /, whichever shell it ends up in) would otherwise cost another round
// trip after the entry script. Preload it and the chunks it imports along with the entry.
function preloadLibraryRoute(): Plugin {
  return {
    name: "reelshelf-preload-library-route",
    apply: "build",
    transformIndexHtml: {
      order: "post",
      handler(html, { bundle }) {
        const chunks = Object.values(bundle ?? {}).filter(
          (output) => output.type === "chunk",
        );
        const library = chunks.find((chunk) =>
          chunk.facadeModuleId?.includes("/src/routes/_library.tsx?"),
        );
        if (!library) {
          throw new Error("No built chunk for routes/_library.tsx");
        }
        const files = new Set<string>();
        const visit = (fileName: string) => {
          if (files.has(fileName)) return;
          files.add(fileName);
          const chunk = chunks.find((output) => output.fileName === fileName);
          chunk?.imports.forEach(visit);
        };
        visit(library.fileName);
        return [...files]
          .filter((file) => !html.includes(`/${file}"`))
          .map((file) => ({
            tag: "link",
            attrs: { rel: "modulepreload", crossorigin: "", href: `/${file}` },
            injectTo: "head",
          }));
      },
    },
  };
}

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
    preloadLibraryRoute(),
    preloadFonts(),
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
