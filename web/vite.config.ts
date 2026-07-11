/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { VitePWA } from 'vite-plugin-pwa'

// Numera PWA config.
// SECURITY / DATA POSTURE (RESEARCH Pattern 7 + Pitfall 5):
//   The service worker precaches ONLY the static app shell (JS/CSS/HTML/fonts/icons).
//   Financial data under /api MUST NEVER be served stale from cache:
//     - navigateFallbackDenylist excludes /api from the SPA navigation fallback
//     - a NetworkOnly runtime rule guarantees /api requests always hit the network
export default defineConfig({
  plugins: [
    react(),
    // Tailwind v4 runs as a Vite plugin (no PostCSS config / no tailwind.config.js).
    tailwindcss(),
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['icons/icon-192.png', 'icons/icon-512.png', 'icons/maskable-512.png'],
      manifest: false, // authored manually in public/manifest.webmanifest
      workbox: {
        // Precache the static app shell only.
        globPatterns: ['**/*.{js,css,html,ico,png,svg,woff,woff2}'],
        // Never let SPA navigation fallback serve /api requests from the shell.
        navigateFallbackDenylist: [/^\/api/],
        runtimeCaching: [
          {
            // All financial reads/writes: always network, never cached.
            urlPattern: /^\/api\/.*/,
            handler: 'NetworkOnly',
            method: 'GET',
          },
          {
            urlPattern: /^\/api\/.*/,
            handler: 'NetworkOnly',
            method: 'POST',
          },
        ],
        cleanupOutdatedCaches: true,
      },
      devOptions: {
        enabled: false,
      },
    }),
  ],
  resolve: {
    // `@/…` maps to web/src (shadcn/ui convention).
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    // Same-origin BFF in dev: SPA and backend share an origin so the HttpOnly
    // session cookie flows on same-origin fetch (credentials: 'include').
    proxy: {
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: true,
        secure: false,
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
  },
})
