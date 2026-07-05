import { defineConfig } from 'vite'
import { devtools } from '@tanstack/devtools-vite'

import { tanstackStart } from '@tanstack/react-start/plugin/vite'

import viteReact from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

const config = defineConfig({
  resolve: { tsconfigPaths: true },
  plugins: [
    devtools({
      injectSource: {
        enabled: true,
        // react-map-gl forwards unknown props into maplibre's addSource/addLayer,
        // whose validators reject the injected data-tsd-source attribute.
        ignore: { components: ['Map', 'Source', 'Layer'] },
      },
    }),
    tailwindcss(),
    tanstackStart(),
    viteReact(),
  ],
})

export default config
