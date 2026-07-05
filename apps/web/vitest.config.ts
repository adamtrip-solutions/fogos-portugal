import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vitest/config'

// Standalone config for unit tests over pure helpers — deliberately does not
// load the TanStack Start / Tailwind plugins (they are irrelevant to the pure
// functions under test and pull SSR concerns into the runner). Files that need
// a DOM (the KML adapter) opt in per-file via `// @vitest-environment jsdom`.
export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
  resolve: {
    alias: {
      '#': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
})
