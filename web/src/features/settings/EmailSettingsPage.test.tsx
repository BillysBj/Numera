import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '@/i18n'
import { getMe } from '@/lib/api'
import { getEmailSettings, saveEmailSettings, sendTestEmail, type EmailSettingsDto } from '@/lib/api/emailSettings'
import EmailSettingsPage from './EmailSettingsPage'

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT: boolean })
  .IS_REACT_ACT_ENVIRONMENT = true

vi.mock('@/lib/api', async (original) => ({ ...await original<typeof import('@/lib/api')>(), getMe: vi.fn() }))
vi.mock('@/lib/api/emailSettings', () => ({ getEmailSettings: vi.fn(), saveEmailSettings: vi.fn(), sendTestEmail: vi.fn() }))

const settings: EmailSettingsDto = {
  host: 'smtp.example.com', port: 587, useSsl: true, username: 'billing', hasPassword: true,
  fromAddress: 'billing@example.com', fromName: 'Example', invoiceSubject: 'Rechnung {Rechnungsnummer}',
  invoiceBody: 'Hallo {Kundenname}', dunningSubject: null, dunningBody: null,
}
let container: HTMLDivElement
let root: Root
let queryClient: QueryClient

async function flush() {
  await act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)) })
}
async function render() {
  await act(async () => {
    root.render(<QueryClientProvider client={queryClient}><EmailSettingsPage /></QueryClientProvider>)
  })
  await flush()
  await flush()
}
function input(name: string) {
  const element = container.querySelector<HTMLInputElement>(`[name="${name}"]`)
  if (!element) throw new Error(`Missing input ${name}`)
  return element
}
async function change(name: string, value: string) {
  await act(async () => {
    const element = input(name)
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(element, value)
    element.dispatchEvent(new Event('input', { bubbles: true }))
    element.dispatchEvent(new Event('change', { bubbles: true }))
  })
}
async function submit(index = 0) {
  await act(async () => {
    container.querySelectorAll('form')[index].dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  })
  await flush()
}

beforeEach(async () => {
  vi.clearAllMocks()
  await i18n.changeLanguage('de')
  vi.mocked(getMe).mockResolvedValue({ user: { sub: 'owner', name: 'Owner', email: 'owner@example.com' }, tenant: null, role: 'Owner' })
  vi.mocked(getEmailSettings).mockResolvedValue(settings)
  vi.mocked(saveEmailSettings).mockResolvedValue(settings)
  vi.mocked(sendTestEmail).mockResolvedValue({ success: true, error: null })
  container = document.createElement('div')
  document.body.appendChild(container)
  root = createRoot(container)
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
})
afterEach(async () => {
  await act(async () => root.unmount())
  queryClient.clear()
  container.remove()
})

describe('EmailSettingsPage', () => {
  it('loads saved fields, reports password presence, and never prefills a password', async () => {
    await render()
    expect(input('host').value).toBe('smtp.example.com')
    expect(input('password').type).toBe('password')
    expect(input('password').value).toBe('')
    expect(container.textContent).toContain('Passwort — gesetzt')
    expect(container.textContent).toContain('{Mahngebühr}')
  })
  it('omits a blank password while saving edited templates', async () => {
    await render()
    await change('invoiceSubject', 'Ihr Beleg {Rechnungsnummer}')
    await submit()
    expect(saveEmailSettings).toHaveBeenCalledWith(expect.objectContaining({ invoiceSubject: 'Ihr Beleg {Rechnungsnummer}', password: undefined }))
    expect(container.textContent).toContain('E-Mail-Einstellungen wurden gespeichert.')
  })
  it('sends a replacement password once and clears it without caching it', async () => {
    await render()
    await change('password', 'replacement-secret')
    await submit()
    expect(saveEmailSettings).toHaveBeenCalledWith(expect.objectContaining({ password: 'replacement-secret' }))
    expect(input('password').value).toBe('')
    expect(JSON.stringify(queryClient.getQueryData(['email-settings']))).not.toContain('replacement-secret')
    expect(queryClient.getMutationCache().getAll()).toHaveLength(0)
  })
  it('tests saved settings and displays SMTP errors as text', async () => {
    await render()
    await submit(1)
    expect(sendTestEmail).toHaveBeenCalledWith('owner@example.com')
    expect(container.textContent).toContain('Testmail wurde erfolgreich gesendet.')
    vi.mocked(sendTestEmail).mockResolvedValue({ success: false, error: '535 <script>SMTP authentication failed</script>' })
    await submit(1)
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('535 <script>')
    expect(container.querySelector('script')).toBeNull()
  })
  it('does not load settings or render the form for an employee', async () => {
    vi.mocked(getMe).mockResolvedValue({ user: { sub: 'employee', name: null, email: null }, tenant: null, role: 'Employee' })
    await render()
    expect(getEmailSettings).not.toHaveBeenCalled()
    expect(container.querySelector('form')).toBeNull()
    expect(container.textContent).toContain('Nur Eigentümer')
  })
  it('uses English copy', async () => {
    await i18n.changeLanguage('en')
    await render()
    expect(container.textContent).toContain('Send test email')
    expect(container.textContent).toContain('Leave blank to keep the saved password.')
  })
})
