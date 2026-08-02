import { useEffect, useState } from 'react'

/**
 * Tracks browser connectivity. The app shell works offline (PWA), but every `/api` call is
 * NetworkOnly (no stale financial reads/writes), so online-only actions must degrade gracefully.
 * Shared by the Dashboard offline banner and the document actions (finalize/send/e-invoice/PDF).
 */
export function useOnline(): boolean {
  const [online, setOnline] = useState(() =>
    typeof navigator === 'undefined' ? true : navigator.onLine,
  )

  useEffect(() => {
    const on = () => setOnline(true)
    const off = () => setOnline(false)
    window.addEventListener('online', on)
    window.addEventListener('offline', off)
    return () => {
      window.removeEventListener('online', on)
      window.removeEventListener('offline', off)
    }
  }, [])

  return online
}
