import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '@/i18n'
import TeamPage from './TeamPage'

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT: boolean })
  .IS_REACT_ACT_ENVIRONMENT = true

let container: HTMLDivElement
let root: Root
let queryClient: QueryClient

async function flush() {
  await act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)) })
}

beforeEach(() => {
  container = document.createElement('div')
  document.body.appendChild(container)
  root = createRoot(container)
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
})

afterEach(async () => {
  await act(async () => root.unmount())
  queryClient.clear()
  container.remove()
  vi.unstubAllGlobals()
})

describe('team invitation delivery', () => {
  it.each([
    ['de', true, 'Einladungs-E-Mail an member@example.test gesendet.'],
    ['de', false, 'Die Einladungs-E-Mail an member@example.test konnte nicht gesendet werden.'],
    ['en', true, 'Invitation email sent to member@example.test.'],
    ['en', false, 'The invitation email to member@example.test could not be sent.'],
  ] as const)('keeps the temporary password with %s emailSent=%s', async (language, emailSent, expected) => {
    await invite(language, emailSent, 'temporary-7Az')
    expect(container.querySelector('[role="status"]')?.textContent).toContain(expected)
    expect(container.querySelector('code')?.textContent).toBe('temporary-7Az')
    expect(container.querySelector('[role="alert"]')).toBeNull()
  })

  it('shows delivery status without a password for an existing user', async () => {
    await invite('en', true, null)
    expect(container.querySelector('[role="status"]')?.textContent)
      .toContain('Invitation email sent to member@example.test.')
    expect(container.querySelector('code')).toBeNull()
  })
})

async function invite(language: string, emailSent: boolean, temporaryPassword: string | null) {
  await i18n.changeLanguage(language)
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (url === '/api/me/entitlements') return Response.json({ capabilities: ['MultiUser'] })
    if (url === '/api/team') return Response.json([])
    if (url === '/api/team/invite' && init?.method === 'POST') {
      return Response.json({ userId: 'new-user', temporaryPassword, emailSent }, { status: 201 })
    }
    throw new Error(`Unexpected request: ${url}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  await act(async () => {
    root.render(<QueryClientProvider client={queryClient}><TeamPage /></QueryClientProvider>)
  })
  await flush()
  await flush()
  await act(async () => {
    const input = container.querySelector<HTMLInputElement>('#team-email')!
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(input, 'member@example.test')
    input.dispatchEvent(new Event('input', { bubbles: true }))
    input.dispatchEvent(new Event('change', { bubbles: true }))
  })
  await act(async () => {
    container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  })
  await flush()
  expect(fetchMock).toHaveBeenCalledWith('/api/team/invite', expect.objectContaining({
    method: 'POST', body: JSON.stringify({ email: 'member@example.test', role: 2 }),
  }))
}
