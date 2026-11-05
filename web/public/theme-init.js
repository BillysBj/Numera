// Apply the persisted theme before first paint to avoid a flash of the wrong theme.
// Externalized from an inline <script> so the Content-Security-Policy can keep a strict
// `script-src 'self'` (no 'unsafe-inline'). Served same-origin and precached by the PWA.
try {
  var t = localStorage.getItem('theme')
  var d = t ? t === 'dark' : matchMedia('(prefers-color-scheme: dark)').matches
  if (d) document.documentElement.classList.add('dark')
} catch (e) {}
