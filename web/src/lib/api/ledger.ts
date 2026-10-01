// Ledger / chart-of-accounts setup BFF client. Same posture as the other api modules:
// no tokens, HttpOnly session cookie, same-origin. Enums cross the wire as NUMBERS.

import { apiRequest, ApiError } from '../api'

export { ApiError }

export const ChartVariant = { Skr03: 3, Skr04: 4 } as const
export const Besteuerungsart = { Soll: 0, Ist: 1 } as const
export const Gewinnermittlungsart = { Euer: 0, Bilanzierung: 1 } as const

export interface LedgerSettings {
  id: string
  chartVariant: number
  besteuerungsart: number
  gewinnermittlungsart: number
  fiscalYearStartMonth: number
}

export interface LedgerSetupRequest {
  chartVariant: number
  besteuerungsart: number
  gewinnermittlungsart: number
  fiscalYearStartMonth: number | null
}

export interface LedgerSetupResponse {
  settings: LedgerSettings
  accountCount: number
}

/** GET /api/ledger/settings — the tenant's ledger config, or null when not set up yet. */
export async function getLedgerSettings(): Promise<LedgerSettings | null> {
  try {
    return await apiRequest<LedgerSettings>('/ledger/settings')
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null
    throw e
  }
}

/** POST /api/ledger/setup — one-time chart-of-accounts + settings creation (Owner only). */
export function setupLedger(req: LedgerSetupRequest): Promise<LedgerSetupResponse> {
  return apiRequest<LedgerSetupResponse>('/ledger/setup', {
    method: 'POST',
    body: JSON.stringify(req),
  })
}
