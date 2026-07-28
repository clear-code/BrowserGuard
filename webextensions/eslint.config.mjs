import js from "@eslint/js";
import globals from "globals";
import { defineConfig } from "eslint/config";


export default defineConfig([
  {
    // ビルド生成物は対象外。
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
      // 先頭が _ の引数・変数は「意図的に未使用」とみなす。
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
