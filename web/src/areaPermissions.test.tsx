import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import './i18n'
import { PermissionRoute, useNavGroups } from './App'
import { AREA_KEYS, canAccessRoute } from './lib/areaPermissions'

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

let container: HTMLDivElement
let root: Root
let queryClient: QueryClient

beforeEach(() => {
  container = document.createElement('div')
  document.body.appendChild(container)
  root = createRoot(container)
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } })
  queryClient.setQueryData(['me'], { role: 'Employee', allowedAreas: ['Documents'] })
  queryClient.setQueryData(['ledger-settings'], { gewinnermittlungsart: 1 })
  queryClient.setQueryData(['billing-status'], { selfHosted: true })
})

afterEach(async () => {
  await act(async () => root.unmount())
  queryClient.clear()
  container.remove()
  vi.unstubAllGlobals()
})

function Nav() {
  return <nav>{useNavGroups().flatMap(group => group.items).map(item => <a key={item.to} href={item.to}>{item.label}</a>)}</nav>
}

describe('employee area access', () => {
  it('hides disallowed areas and owner-only nav items', async () => {
    await act(async () => root.render(<QueryClientProvider client={queryClient}><Nav /></QueryClientProvider>))
    expect(container.querySelector('a[href="/documents"]')).not.toBeNull()
    expect(container.querySelector('a[href="/dashboard"]')).not.toBeNull()
    for (const path of ['/partners', '/banking', '/belege', '/inbound', '/reports/ustva', '/reports/datev', '/settings', '/settings/ledger', '/team']) {
      expect(container.querySelector(`a[href="${path}"]`)).toBeNull()
    }
  })

  it.each(['/partners/new', '/belege/123', '/settings', '/SETTINGS/', '/settings/ledger', '/settings/email', '/team', '/reports/datev'])(
    'redirects a forbidden direct route %s before mounting it', async path => {
      const mounted = vi.fn()
      function ProtectedPage() { mounted(); return <p>Protected page</p> }
      await act(async () => root.render(
        <QueryClientProvider client={queryClient}>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route element={<PermissionRoute />}><Route path={path} element={<ProtectedPage />} /></Route>
              <Route path="/dashboard" element={<p>Dashboard</p>} />
            </Routes>
          </MemoryRouter>
        </QueryClientProvider>,
      ))
      expect(container.textContent).toBe('Dashboard')
      expect(mounted).not.toHaveBeenCalled()
    },
  )

  it('preserves null/default access, bypasses Owner/TaxAdvisor areas and keeps owner routes private', () => {
    for (const role of ['Employee', 'Owner', 'TaxAdvisor']) {
      expect(canAccessRoute({ role, allowedAreas: null }, '/partners')).toBe(true)
    }
    expect(canAccessRoute({ role: 'Owner', allowedAreas: [] }, '/partners')).toBe(true)
    expect(canAccessRoute({ role: 'TaxAdvisor', allowedAreas: [] }, '/documents')).toBe(true)
    expect(canAccessRoute({ role: 'TaxAdvisor', allowedAreas: null }, '/settings')).toBe(false)
    expect(canAccessRoute({ role: 'Employee', allowedAreas: [...AREA_KEYS] }, '/reports/datev')).toBe(false)
    expect(canAccessRoute({ role: 'Employee', allowedAreas: ['Dunning'] }, '/settings/dunning')).toBe(true)
    expect(canAccessRoute({ role: 'Employee', allowedAreas: [] }, '/dashboard')).toBe(true)
  })
})
