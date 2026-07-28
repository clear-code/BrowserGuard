import js from "@eslint/js";
import globals from "globals";
import { defineConfig } from "eslint/config";


export default defineConfig([
  {
    // Exclude build artifacts.
    ignores: ["**/dev/**", ".build/**"],
  },
  {
    files: ["**/*.{js,mjs,cjs}"],
    plugins: { js },
    extends: ["js/recommended"]
  },
  {
    files: ["**/*.{js,mjs,cjs}"],
    rules: {
      // Treat a leading underscore as "intentionally unused".
      "no-unused-vars": ["error", {
        argsIgnorePattern: "^_",
        varsIgnorePattern: "^_",
        caughtErrorsIgnorePattern: "^_",
      }],
    },
  },
  { 
    files: ["**/*.{js,mjs,cjs}"], 
    languageOptions: {
      globals: { 
        ...globals.browser,
        chrome: "readonly",
        module: "readonly",
        exports: "readonly",
      }
    }
  },
]);
