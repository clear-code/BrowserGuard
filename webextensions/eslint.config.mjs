/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

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
