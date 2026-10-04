import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import i18n from '@/i18n'
import { DocumentStatus, DocumentType, type SalesDocumentDetail } from '@/lib/api/documents'
import DocumentDetailPage from './DocumentDetailPage'

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT: boolean })
  .IS_REACT_ACT_ENVIRONMENT = true

let container: HTMLDivElement
let root: Root
let queryClient: QueryClient
const fetchMock = vi.fn<typeof fetch>()
let documents: Record<string, SalesDocumentDetail>

function invoice(id: string, recipientLanguage?: 'de' | 'en'): SalesDocumentDetail {
  return {
    id, recipientLanguage, documentType: DocumentType.Rechnung, status: DocumentStatus.Finalized,
    documentNumber: 'RE-42', documentDate: '2026-10-04', currency: 'EUR',
    totalNet: 100, totalTax: 19, totalGross: 119, amountDue: 119,
    isKleinunternehmer: false, reverseCharge: false, lines: [], taxBreakdown: [], prepayments: [],
  }
}

beforeEach(async () => {
  await i18n.changeLanguage('de')
  documents = { english: invoice('english', 'en'), legacy: invoice('legacy') }
  fetchMock.mockReset()
  fetchMock.mockImplementation(async (input, init) => {
    const url = String(input)
    if (init?.method === 'POST') return new Response(JSON.stringify({ id: 'email-1', status: 0 }))
    if (url.endsWith('/entitlements')) return new Response(JSON.stringify({ capabilities: ['EInvoicing'] }))
    const id = url.split('/').at(-1)!
    return new Response(JSON.stringify(documents[id]))
  })
  vi.stubGlobal('fetch', fetchMock)
  vi.stubGlobal('confirm', vi.fn(() => true))
  container = document.createElement('div')
  document.body.appendChild(container)
  root = createRoot(container)
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
})

afterEach(async () => {
  await act(async () => root.unmount())
  container.remove()
  queryClient.clear()
  vi.unstubAllGlobals()
})

async function settle() {
  await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)) })
}

async function render(id = 'english') {
  await act(async () => {
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/documents/${id}`]}>
          <Link to="/documents/english">English document</Link>
          <Link to="/documents/legacy">Legacy document</Link>
          <Routes><Route path="/documents/:id" element={<DocumentDetailPage />} /></Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    )
  })
  await settle()
}

function languageSelect() {
  return [...container.querySelectorAll('select')].find((select) => select.querySelector('option[value="en"]'))!
}

async function chooseGerman() {
  await act(async () => {
    languageSelect().value = 'de'
    languageSelect().dispatchEvent(new Event('change', { bubbles: true }))
  })
}

async function send(action: 'send' | 'sendEInvoice') {
  await act(async () => {
    const label = i18n.t(`actions.${action}`, { ns: 'documents' })
    const button = [...container.querySelectorAll('button')].find((candidate) => candidate.textContent === label)
    expect(button).toBeDefined()
    button!.click()
  })
  await settle()
}

it('defaults to the asynchronously loaded recipient language and sends both mail types in that language', async () => {
  await render()
  expect(languageSelect().value).toBe('en')
  await send('send')
  await send('sendEInvoice')
  const posts = fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')
  expect(posts.map(([url]) => String(url).split('/').at(-1))).toEqual(['send', 'send-einvoice'])
  expect(posts.map(([, init]) => JSON.parse(String(init?.body)))).toEqual([{ language: 'en' }, { language: 'en' }])
})

it('keeps a manual override across refreshes and uses it for both send actions', async () => {
  await render()
  await chooseGerman()
  await act(async () => { await queryClient.invalidateQueries({ queryKey: ['document', 'english'] }) })
  await settle()
  expect(languageSelect().value).toBe('de')
  await send('send')
  await send('sendEInvoice')
  const posts = fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')
  expect(posts.map(([, init]) => JSON.parse(String(init?.body)))).toEqual([{ language: 'de' }, { language: 'de' }])
})

it('falls back to German for old responses and resets manual overrides when navigating', async () => {
  await render()
  await chooseGerman()
  await act(async () => { container.querySelector<HTMLAnchorElement>('a[href="/documents/legacy"]')!.click() })
  await settle()
  expect(languageSelect().value).toBe('de')
  await act(async () => { container.querySelector<HTMLAnchorElement>('a[href="/documents/english"]')!.click() })
  await settle()
  expect(languageSelect().value).toBe('en')
})
