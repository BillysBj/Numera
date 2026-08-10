import { useQuery } from '@tanstack/react-query'

/**
 * Mirrors the server enum ordinals. Capability names remain the wire format of
 * GET /api/me/entitlements.
 */
export const Capability = {
  MultiUser: 1,
  DataExport: 2,
  Dunning: 3,
  EInvoicing: 4,
  ApiAccess: 5,
  ForeignCurrencyInvoicing: 6,
  RecurringInvoices: 7,
  DownPaymentInvoices: 8,
} as const

export type CapabilityName = keyof typeof Capability

export type PlanName = 'Free' | 'S' | 'M' | 'L' | 'XL'

export interface Entitlements {
  capabilities: CapabilityName[]
}

async function fetchEntitlements(): Promise<Entitlements> {
  const response = await fetch('/api/me/entitlements', {
    credentials: 'include',
    headers: { Accept: 'application/json' },
  })

  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`)
  }

  return response.json() as Promise<Entitlements>
}

/** Cosmetic entitlement state only; server-side gates remain authoritative. */
export function useEntitlements() {
  return useQuery({
    queryKey: ['entitlements'],
    queryFn: fetchEntitlements,
  })
}

export function hasCapability(
  capabilities: readonly CapabilityName[] | null | undefined,
  capability: CapabilityName,
): boolean {
  return capabilities?.includes(capability) ?? false
}
