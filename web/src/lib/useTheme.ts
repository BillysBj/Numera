import { useCallback, useEffect, useState } from 'react'

type Theme = 'light' | 'dark'

function currentTheme(): Theme {
  if (typeof document === 'undefined') return 'light'
  return document.documentElement.classList.contains('dark') ? 'dark' : 'light'
}

/**
 * Light/dark theme toggle. The initial class is applied by an inline script in
 * index.html (before first paint, no flash); this hook keeps React in sync and
 * persists the choice. Toggling flips the `dark` class on <html>.
 */
export function useTheme(): { theme: Theme; toggle: () => void } {
  const [theme, setTheme] = useState<Theme>(currentTheme)

  useEffect(() => {
    const root = document.documentElement
    root.classList.toggle('dark', theme === 'dark')
    try {
      localStorage.setItem('theme', theme)
    } catch {
      /* storage may be unavailable (private mode) */
    }
  }, [theme])

  const toggle = useCallback(
    () => setTheme((t) => (t === 'dark' ? 'light' : 'dark')),
    [],
  )

  return { theme, toggle }
}
