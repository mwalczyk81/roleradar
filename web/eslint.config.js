import js from "@eslint/js";
import tseslint from "typescript-eslint";
import reactPlugin from "eslint-plugin-react";

const tokenDisciplinePlugin = {
  rules: {
    "no-raw-hex-or-px": {
      meta: {
        type: "problem",
        docs: {
          description: "Forbid raw hex colors and explicit pixel values outside web/app/ds",
        },
        schema: [],
        messages: {
          noHex: "Raw hex color '{{value}}' is forbidden. Use semantic --ds-* tokens instead.",
          noPx: "Explicit px dimension '{{value}}' is forbidden outside web/app/ds. Use semantic spacing tokens instead.",
        },
      },
      create(context) {
        // Only enforce on app files outside ds
        const filename = context.filename || context.getFilename();
        const normalized = filename.replace(/\\/g, "/");
        if (!normalized.includes("/app/") || normalized.includes("/app/ds/")) {
          return {};
        }

        const hexRegex = /#[0-9a-fA-F]{3,8}\b/;
        const pxRegex = /\b\d+px\b/;

        function checkString(node, value) {
          if (typeof value !== "string") return;
          const hexMatch = value.match(hexRegex);
          if (hexMatch) {
            context.report({
              node,
              messageId: "noHex",
              data: { value: hexMatch[0] },
            });
          }
          const pxMatch = value.match(pxRegex);
          if (pxMatch) {
            context.report({
              node,
              messageId: "noPx",
              data: { value: pxMatch[0] },
            });
          }
        }

        return {
          Literal(node) {
            if (typeof node.value === "string") {
              checkString(node, node.value);
            }
          },
          TemplateElement(node) {
            if (node.value && node.value.raw) {
              checkString(node, node.value.raw);
            }
          },
        };
      },
    },
  },
};

export default tseslint.config(
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    plugins: {
      react: reactPlugin,
      "token-discipline": tokenDisciplinePlugin,
    },
    languageOptions: {
      parserOptions: {
        ecmaFeatures: {
          jsx: true,
        },
      },
    },
    settings: {
      react: {
        version: "19.0",
      },
    },
    rules: {
      "react/no-danger": "error",
      "token-discipline/no-raw-hex-or-px": "error",
      "@typescript-eslint/no-explicit-any": "off",
      "@typescript-eslint/no-unused-vars": [
        "warn",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
    },
  },
  {
    files: ["app/root.tsx"],
    rules: {
      // Per ADR-0026, the nonced theme script in root.tsx is the sole legitimate use of dangerouslySetInnerHTML
      "react/no-danger": "off",
    },
  },
  {
    ignores: [
      "dist/**",
      "build/**",
      ".react-router/**",
      "node_modules/**",
      "app/ds/**",
    ],
  }
);
