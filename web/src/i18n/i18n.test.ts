import { describe, it, expect, beforeEach } from 'vitest'
import i18n from './index'

describe('i18n (DE/EN)', () => {
  beforeEach(async () => {
    localStorage.clear()
    await i18n.changeLanguage('de')
  })

  it('defaults to German and resolves shell strings', () => {
    expect(i18n.resolvedLanguage).toBe('de')
    expect(i18n.t('nav.login', { ns: 'common' })).toBe('Anmelden')
    expect(i18n.t('login.signInButton', { ns: 'auth' })).toBe('Anmelden')
  })

  it('switches visible strings to English', async () => {
    await i18n.changeLanguage('en')
    expect(i18n.resolvedLanguage).toBe('en')
    expect(i18n.t('nav.login', { ns: 'common' })).toBe('Sign in')
    expect(i18n.t('register.title', { ns: 'auth' })).toBe('Register company')
  })

  it('persists the chosen language to localStorage (lng)', async () => {
    await i18n.changeLanguage('en')
    expect(localStorage.getItem('lng')).toBe('en')
    await i18n.changeLanguage('de')
    expect(localStorage.getItem('lng')).toBe('de')
  })

  it('exposes both de and en for common, auth, reports, and belege namespaces', () => {
    for (const lng of ['de', 'en']) {
      expect(i18n.hasResourceBundle(lng, 'common')).toBe(true)
      expect(i18n.hasResourceBundle(lng, 'auth')).toBe(true)
      expect(i18n.hasResourceBundle(lng, 'reports')).toBe(true)
      expect(i18n.hasResourceBundle(lng, 'belege')).toBe(true)
    }
  })
})
