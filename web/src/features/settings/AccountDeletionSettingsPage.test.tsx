import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '@/i18n'
import { getMe } from '@/lib/api'
import {
  cancelAccountDeletion,
  getAccountDeletionStatus,
  requestAccountDeletion,
  type AccountDeletionStatus,
} from '@/lib/api/accountDeletion'
import AccountDeletionSettingsPage from './AccountDeletionSettingsPage'

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

vi.mock('@/lib/api', async (original) => ({
  ...await original<typeof import('@/lib/api')>(),
  getMe: vi.fn(),
  apiBlob: vi.fn(),
}))
vi.mock('@/lib/api/accountDeletion', () => ({
  getAccountDeletionStatus: vi.fn(),
  requestAccountDeletion: vi.fn(),
  cancelAccountDeletion: vi.fn(),
}))

const base: AccountDeletionStatus = {
  pendingDeletion: false,
  requestedAt: null,
  scheduledFor: null,
  graceDays: 30,
  lastExportAt: null,
  hasFreshExport: false,
}

let container: HTMLDivElement
let root: Root
let queryClient: QueryClient

async function flush() {
  await act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)) })
}
async function render() {
  await act(async () => {
    root.render(<QueryClientProvider client={queryClient}><AccountDeletionSettingsPage /></QueryClientProvider>)
  })
  await flush()
  await flush()
}
function button(label: string): HTMLButtonElement {
  const match = [...container.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)
  if (!match) throw new Error(`Missing button "${label}". Have: ${[...container.querySelectorAll('button')].map((b) => b.textContent).join(' | ')}`)
  return match as HTMLButtonElement
}
async function type(id: string, value: string) {
  await act(async () => {
    const element = container.querySelector<HTMLInputElement>(`#${id}`)!
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(element, value)
    element.dispatchEvent(new Event('input', { bubbles: true }))
    element.dispatchEvent(new Event('change', { bubbles: true }))
  })
}

beforeEach(async () => {
  vi.clearAllMocks()
  await i18n.changeLanguage('de')
  vi.mocked(getMe).mockResolvedValue({ user: { sub: 'o', name: 'O', email: 'o@x.de' }, tenant: { id: 't', name: 'NOEMA UG', plan: 'M' }, role: 'Owner', allowedAreas: null })
  vi.mocked(cancelAccountDeletion).mockResolvedValue(base)
  vi.mocked(requestAccountDeletion).mockResolvedValue({ ...base, pendingDeletion: true, scheduledFor: '2026-12-04T00:00:00Z' })
  container = document.createElement('div')
  document.body.appendChild(container)
  root = createRoot(container)
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
})
afterEach(() => {
  act(() => root.unmount())
  container.remove()
})

describe('AccountDeletionSettingsPage', () => {
  it('blocks the request until a fresh export exists', async () => {
    vi.mocked(getAccountDeletionStatus).mockResolvedValue({ ...base, hasFreshExport: false })
    await render()
    expect(button('Konto zur Löschung vormerken').disabled).toBe(true)
  })

  it('requests deletion with the typed confirmation name when export is fresh', async () => {
    vi.mocked(getAccountDeletionStatus).mockResolvedValue({ ...base, hasFreshExport: true, lastExportAt: '2026-11-04T00:00:00Z' })
    await render()
    await type('confirm-name', 'NOEMA UG')
    const submit = button('Konto zur Löschung vormerken')
    expect(submit.disabled).toBe(false)
    await act(async () => { submit.click() })
    await flush()
    expect(requestAccountDeletion).toHaveBeenCalledWith('NOEMA UG')
  })

  it('shows the pending state with a cancel action', async () => {
    vi.mocked(getAccountDeletionStatus).mockResolvedValue({ ...base, pendingDeletion: true, scheduledFor: '2026-12-04T00:00:00Z' })
    await render()
    const cancel = button('Löschung abbrechen')
    await act(async () => { cancel.click() })
    await flush()
    expect(cancelAccountDeletion).toHaveBeenCalled()
  })
})
