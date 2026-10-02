import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import i18n from '@/i18n'
import PartnerDocumentsSection from './PartnerDocumentsSection'

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT: boolean })
  .IS_REACT_ACT_ENVIRONMENT = true

let container: HTMLDivElement
let root: Root
let queryClient: QueryClient
const fetchMock = vi.fn<typeof fetch>()

beforeEach(async () => {
  await i18n.changeLanguage('de')
  fetchMock.mockReset()
  vi.stubGlobal('fetch', fetchMock)
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

async function render() {
  await act(async () => {
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <PartnerDocumentsSection partnerId="partner-42" />
        </MemoryRouter>
      </QueryClientProvider>,
    )
  })
  await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)) })
}

it('requests partner-filtered pages and links documents, with a dash for draft gross', async () => {
  fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({
    items: [
      { id: 'invoice-1', documentNumber: 'RE-2026-1', documentType: 3, status: 1, totalGross: 119, currency: 'EUR' },
      { id: 'draft-1', documentNumber: null, documentType: 3, status: 0, totalGross: 0, currency: 'EUR' },
    ], page: 1, pageSize: 5, total: 6,
  })))
  fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({
    items: [{ id: 'invoice-6', documentNumber: 'RE-2026-6', documentType: 3, status: 1, totalGross: 107, currency: 'EUR' }],
    page: 2, pageSize: 5, total: 6,
  })))
  await render()

  const url = new URL(String(fetchMock.mock.calls[0][0]), 'http://localhost')
  expect(url.searchParams.get('partnerId')).toBe('partner-42')
  expect(url.searchParams.get('pageSize')).toBe('5')
  expect(container.querySelector('a')?.getAttribute('href')).toBe('/documents/invoice-1')
  expect(container.textContent).toContain('119,00')
  const draftRow = container.querySelector('a[href="/documents/draft-1"]')?.closest('tr')
  expect(draftRow?.lastElementChild?.textContent).toBe('—')
  expect(container.textContent).not.toContain('Phase')

  await act(async () => {
    const next = [...container.querySelectorAll('button')].find((button) => button.textContent === 'Weiter')
    expect(next).toBeDefined()
    next!.click()
  })
  await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)) })
  const nextUrl = new URL(String(fetchMock.mock.calls[1][0]), 'http://localhost')
  expect(nextUrl.searchParams.get('partnerId')).toBe('partner-42')
  expect(nextUrl.searchParams.get('page')).toBe('2')
  expect(container.querySelector('a')?.getAttribute('href')).toBe('/documents/invoice-6')
})

it('shows an empty state when the partner has no documents', async () => {
  fetchMock.mockResolvedValue(new Response(JSON.stringify({ items: [], page: 1, pageSize: 5, total: 0 })))
  await render()
  expect(container.textContent).toContain('Keine Belege gefunden.')
})

it('shows a load error when the document request fails', async () => {
  fetchMock.mockResolvedValue(new Response('{}', { status: 500 }))
  await render()
  expect(container.querySelector('[role="alert"]')?.textContent).toBe('Beleg konnte nicht geladen werden.')
})
