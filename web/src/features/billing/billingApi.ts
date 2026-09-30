import { useQuery } from '@tanstack/react-query'

import { apiRequest } from '@/lib/api'
import type { PlanName } from '@/lib/entitlements'

export interface BillingStatus {
  selfHosted: boolean
  plan: PlanName
  trialEndsAt: string | null
  subscriptionStatus: string | null
  hasSubscription: boolean
  trialActive: boolean
  degraded: boolean
}

interface HostedSessionResponse {
  url: string
}

export function useBillingStatus() {
  return useQuery({
    queryKey: ['billing-status'],
    queryFn: () => apiRequest<BillingStatus>('/billing/status'),
  })
}

export async function startCheckout(plan: PlanName): Promise<void> {
  const session = await apiRequest<HostedSessionResponse>('/billing/checkout', {
    method: 'POST',
    body: JSON.stringify({ plan }),
  })
  window.location.assign(session.url)
}

export async function openPortal(): Promise<void> {
  const session = await apiRequest<HostedSessionResponse>('/billing/portal', {
    method: 'POST',
  })
  window.location.assign(session.url)
}
