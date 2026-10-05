import { useTranslation } from 'react-i18next'
import { SUPPORTED_LANGUAGES, type SupportedLanguage } from '../i18n'
import { cn } from '../lib/utils'

// Persist the chosen language so the BFF/SSR agree on the locale.
// i18next-browser-languagedetector already caches to localStorage + cookie,
// but we set the cookie explicitly with a sane path/max-age so it survives.
function persistLng(lng: SupportedLanguage) {
  try {
    localStorage.setItem('lng', lng)
  } catch {
    /* storage may be unavailable (private mode) — cookie still applies */
  }
  const oneYear = 60 * 60 * 24 * 365
  document.cookie = `lng=${lng}; path=/; max-age=${oneYear}; SameSite=Lax`
}

export function LanguageSwitcher({ className }: { className?: string }) {
  const { i18n, t } = useTranslation('common')
  // i18n.resolvedLanguage collapses "de-DE" → "de".
  const active = (i18n.resolvedLanguage ?? i18n.language) as string

  const change = (lng: SupportedLanguage) => {
    if (active === lng) return
    void i18n.changeLanguage(lng)
    persistLng(lng)
    document.documentElement.lang = lng
  }

  return (
    <div
      className={cn(
        'inline-flex items-center rounded-lg border border-border bg-secondary/60 p-0.5',
        className,
      )}
      role="group"
      aria-label={t('language.label')}
    >
      {SUPPORTED_LANGUAGES.map((lng) => {
        const isActive = active === lng
        return (
          <button
            key={lng}
            type="button"
            aria-pressed={isActive}
            onClick={() => change(lng)}
            className={cn(
              'rounded-[0.4rem] px-2.5 py-1 text-xs font-semibold transition-colors',
              isActive
                ? 'bg-card text-foreground shadow-sm'
                : 'text-muted-foreground hover:text-foreground',
            )}
          >
            {t(`language.${lng}`)}
          </button>
        )
      })}
    </div>
  )
}

export default LanguageSwitcher
