import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import LanguageDetector from 'i18next-browser-languagedetector'

import deCommon from './locales/de/common.json'
import enCommon from './locales/en/common.json'
import deAuth from './locales/de/auth.json'
import enAuth from './locales/en/auth.json'
import dePartners from './locales/de/partners.json'
import enPartners from './locales/en/partners.json'
import deCatalog from './locales/de/catalog.json'
import enCatalog from './locales/en/catalog.json'
import deDocuments from './locales/de/documents.json'
import enDocuments from './locales/en/documents.json'
import deOpenItems from './locales/de/openItems.json'
import enOpenItems from './locales/en/openItems.json'
import deSettings from './locales/de/settings.json'
import enSettings from './locales/en/settings.json'
import deInbound from './locales/de/inbound.json'
import enInbound from './locales/en/inbound.json'

// Supported UI languages. German is the default/fallback (Numera is a German
// financial product); English is the secondary locale (success criterion 5).
export const SUPPORTED_LANGUAGES = ['de', 'en'] as const
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number]

// Namespaces are split so future feature areas can lazy-load their own strings.
export const resources = {
  de: {
    common: deCommon,
    auth: deAuth,
    partners: dePartners,
    catalog: deCatalog,
    documents: deDocuments,
    openItems: deOpenItems,
    settings: deSettings,
    inbound: deInbound,
  },
  en: {
    common: enCommon,
    auth: enAuth,
    partners: enPartners,
    catalog: enCatalog,
    documents: enDocuments,
    openItems: enOpenItems,
    settings: enSettings,
    inbound: enInbound,
  },
} as const

// eslint-disable-next-line @typescript-eslint/no-floating-promises
i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources,
    fallbackLng: 'de',
    supportedLngs: SUPPORTED_LANGUAGES as unknown as string[],
    nonExplicitSupportedLngs: true, // treat "de-DE" as "de"
    ns: ['common', 'auth', 'partners', 'catalog', 'documents', 'openItems', 'inbound', 'settings'],
    defaultNS: 'common',
    interpolation: {
      escapeValue: false, // React already escapes
    },
    detection: {
      // Prefer an explicit prior choice (localStorage / cookie) over the
      // navigator language; the cookie lets the BFF/SSR agree on the locale
      // (RESEARCH Pattern 6).
      order: ['localStorage', 'cookie', 'navigator'],
      lookupLocalStorage: 'lng',
      lookupCookie: 'lng',
      caches: ['localStorage', 'cookie'],
    },
  })

export default i18n
