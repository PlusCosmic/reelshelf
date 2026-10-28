import js from "@eslint/js";
import prettier from "eslint-config-prettier";
import { globalIgnores } from "eslint/config";
import tseslint from "typescript-eslint";
import noJsxCopy from "./eslint-rules/no-jsx-copy.js";

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
  {
    // Copy lives in src/copy/ so it can be reviewed in one place (src/copy/README.md).
    files: ["src/**/*.tsx"],
    ignores: ["src/copy/**", "src/**/*.test.tsx"],
    plugins: { reelshelf: { rules: { "no-jsx-copy": noJsxCopy } } },
    rules: {
      "reelshelf/no-jsx-copy": "error",
    },
  },
];
