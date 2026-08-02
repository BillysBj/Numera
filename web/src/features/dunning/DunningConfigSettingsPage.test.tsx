import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '@/i18n'
import {
  getConfig,
  runDunning,
  saveConfig,
  type DunningLevelConfigDto,
} from '@/lib/api/dunning'
import { listOpenItems, OpenItemStatus } from '@/lib/api/openItems'
import OpenItemsListPage from '@/features/openItems/OpenItemsListPage'
import DunningConfigSettingsPage from './DunningConfigSettingsPage'
import { makeDunningConfigSchema } from './dunningConfigSchema'

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT: boolean })
  .IS_REACT_ACT_ENVIRONMENT = true

vi.mock('@/lib/api/dunning', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api/dunning')>()
  return {
    ...actual,
    getConfig: vi.fn(),
    saveConfig: vi.fn(),
    runDunning: vi.fn(),
  }
})

vi.mock('@/lib/api/openItems', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api/openItems')>()
  return { ...actual, listOpenItems: vi.fn() }
})

// The dunning surfaces are plan L+ (09-05): grant the Dunning capability so the config form and
// the Mahnlauf button are enabled (an L-tier tenant). hasCapability stays real.
vi.mock('@/lib/entitlements', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/entitlements')>()
  return {
    ...actual,
    useEntitlements: () =>
      ({ data: { capabilities: ['Dunning'] } }) as unknown as ReturnType<
        typeof actual.useEntitlements
      >,
  }
})

const mockedGetConfig = vi.mocked(getConfig)
const mockedSaveConfig = vi.mocked(saveConfig)
const mockedRunDunning = vi.mocked(runDunning)
const mockedListOpenItems = vi.mocked(listOpenItems)

const levels: DunningLevelConfigDto[] = [
  {
    level: 0,
    name: 'Zahlungserinnerung',
    daysAfterDue: 7,
    fee: 0,
    chargeInterest: false,
    interestRatePercent: 8.27,
    templateTextDe: 'Bitte zahlen Sie den offenen Betrag.',
    templateTextEn: 'Please pay the outstanding amount.',
  },
  {
    level: 1,
    name: '1. Mahnung',
    daysAfterDue: 14,
    fee: 5,
    chargeInterest: true,
    interestRatePercent: 8.27,
    templateTextDe: 'Wir mahnen den offenen Betrag an.',
    templateTextEn: 'This is a reminder for the outstanding amount.',
  },
]

let container: HTMLDivElement
let root: Root
let queryClient: QueryClient

async function render(component: React.ReactNode) {
  await act(async () => {
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>{component}</MemoryRouter>
      </QueryClientProvider>,
    )
    await Promise.resolve()
  })
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0))
  })
}

function input(name: string): HTMLInputElement {
  const element = container.querySelector<HTMLInputElement>(`[name="${name}"]`)
  if (!element) throw new Error(`Input not found: ${name}`)
  return element
}

function change(element: HTMLInputElement, value: string) {
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(
    element,
    value,
  )
  element.dispatchEvent(new Event('change', { bubbles: true }))
  element.dispatchEvent(new Event('input', { bubbles: true }))
}

async function submit() {
  const form = container.querySelector('form')
  if (!form) throw new Error('Form not found')
  await act(async () => {
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await new Promise((resolve) => setTimeout(resolve, 0))
  })
}

beforeEach(async () => {
  mockedGetConfig.mockReset()
  mockedSaveConfig.mockReset()
  mockedRunDunning.mockReset()
  mockedListOpenItems.mockReset()
  await i18n.changeLanguage('de')
  container = document.createElement('div')
  document.body.appendChild(container)
  root = createRoot(container)
  queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
})

afterEach(async () => {
  await act(async () => root.unmount())
  container.remove()
  queryClient.clear()
})

describe('DunningConfigSettingsPage', () => {
  it('prefills the ladder from getConfig', async () => {
    mockedGetConfig.mockResolvedValue(levels)
    await render(<DunningConfigSettingsPage />)

    expect(mockedGetConfig).toHaveBeenCalledOnce()
    expect(input('levels.0.name').value).toBe('Zahlungserinnerung')
    expect(input('levels.1.fee').value).toBe('5')
  })

  it('blocks a non-contiguous and negative ladder client-side', async () => {
    const schema = makeDunningConfigSchema((key) =>
      i18n.t(key, { ns: 'dunning' }),
    )
    const result = schema.safeParse({
      levels: [
        { ...levels[0], daysAfterDue: -1 },
        { ...levels[1], level: 3 },
      ],
    })

    expect(result.success).toBe(false)
    if (result.success) throw new Error('Invalid ladder unexpectedly passed')
    expect(result.error.issues.map((issue) => issue.message)).toContain(
      'Die Mahnstufen müssen lückenlos bei 0 beginnen.',
    )
    expect(result.error.issues.map((issue) => issue.message)).toContain(
      'Fristen müssen nichtnegativ und nach Stufe nicht fallend sein.',
    )
    expect(mockedSaveConfig).not.toHaveBeenCalled()
  })

  it('saves an edited valid ladder', async () => {
    mockedGetConfig.mockResolvedValue(levels)
    mockedSaveConfig.mockImplementation(async (value) => value)
    await render(<DunningConfigSettingsPage />)
    change(input('levels.1.fee'), '7.5')
    await submit()

    expect(mockedSaveConfig).toHaveBeenCalledWith([
      levels[0],
      { ...levels[1], fee: 7.5 },
    ])
    expect(container.textContent).toContain('Mahnstufen wurden gespeichert.')
  })
})

describe('OpenItemsListPage dunning controls', () => {
  beforeEach(() => {
    mockedListOpenItems.mockResolvedValue({
      page: 1,
      pageSize: 25,
      total: 1,
      items: [
        {
          id: 'open-item-id',
          documentId: 'document-id',
          documentNumber: 'RE-2026-0042',
          partnerId: null,
          currency: 'EUR',
          originalAmount: 100,
          openAmount: 100,
          status: OpenItemStatus.Open,
          issuedOn: '2026-06-01',
          dueDate: '2026-06-15',
          overdue: true,
          currentDunningLevel: 2,
          lastDunnedOn: '2026-07-01',
        },
      ],
    })
  })

  it('renders the dunning-level column and runs dunning with its summary', async () => {
    mockedRunDunning.mockResolvedValue({ issued: 1, skipped: 2 })
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    await render(<OpenItemsListPage />)

    expect(container.textContent).toContain('Mahnstufe')
    expect(container.textContent).toContain('1. Mahnung')
    const button = [...container.querySelectorAll('button')].find(
      (item) => item.textContent === 'Mahnlauf starten',
    )
    if (!button) throw new Error('Dunning run button not found')
    await act(async () => {
      button.click()
      await new Promise((resolve) => setTimeout(resolve, 0))
    })

    expect(mockedRunDunning).toHaveBeenCalledOnce()
    expect(container.textContent).toContain('1 erzeugt, 2 übersprungen')
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['open-items'] })
  })
})
