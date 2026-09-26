import js from "@eslint/js";
import prettier from "eslint-config-prettier";
import { globalIgnores } from "eslint/config";
import tseslint from "typescript-eslint";

export default [
  globalIgnores([
    "dist/**",
    "coverage/**",
    "node_modules/**",
    // Generated code; regenerate instead of editing (see AGENTS.md).
    "src/api-client/**",
    "src/routeTree.gen.ts",
  ]),
  js.configs.recommended,
  ...tseslint.configs.recommended,
  prettier,
  {
    files: ["**/*.{ts,tsx}"],
    rules: {
      "@typescript-eslint/no-non-null-assertion": "off",
    },
  },
];
