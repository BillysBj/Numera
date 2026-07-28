import { API_BASE, ApiError } from '../api'
import type { TaxCategory } from './documents'

export { ApiError }

/** Accept both ASP.NET's default 400 and BFF-normalized 422 validation problems. */
export function extractValidationErrors(
  error: unknown,
): Record<string, string> | null {
  if (
    !(error instanceof ApiError) ||
    (error.status !== 400 && error.status !== 422)
  )
    return null
  const body = error.body as { errors?: Record<string, string[]> } | undefined
  if (!body?.errors) return null
  return Object.fromEntries(
    Object.entries(body.errors)
      .filter(([, messages]) => messages.length > 0)
      .map(([field, messages]) => [field, messages[0]]),
  )
}

export const RecurringIntervalUnit = {
  Monthly: 0,
  Quarterly: 1,
  Yearly: 2,
  Weekly: 3,
} as const
export type RecurringIntervalUnit =
  (typeof RecurringIntervalUnit)[keyof typeof RecurringIntervalUnit]

export const RecurringEndMode = {
  Never: 0,
  UntilDate: 1,
  AfterCount: 2,
} as const
export type RecurringEndMode =
  (typeof RecurringEndMode)[keyof typeof RecurringEndMode]

export const RecurringStatus = {
  Active: 0,
  Paused: 1,
  Ended: 2,
} as const
export type RecurringStatus =
  (typeof RecurringStatus)[keyof typeof RecurringStatus]

export interface RecurringTemplateLineRequest {
  catalogItemId?: string | null
  name: string
  description?: string | null
  quantity: number
  unitCode: string
  netUnitPrice: number
  taxCategory: TaxCategory
  vatRatePercent: number
}

export interface RecurringTemplateLine extends RecurringTemplateLineRequest {
  id: string
  lineNumber: number
}

export interface RecurringTemplate {
  id: string
  name: string
  partnerId?: string | null
  currency: string
  exchangeRate?: number | null
  exchangeRateDate?: string | null
  intervalUnit: RecurringIntervalUnit
  intervalCount: number
  startOn: string
  endMode: RecurringEndMode
  endDate?: string | null
  maxOccurrences?: number | null
  nextRunOn: string
  lastGeneratedPeriodEnd?: string | null
  generatedCount: number
  status: RecurringStatus
  autoFinalize: boolean
  autoSend: boolean
  lines: RecurringTemplateLine[]
}

export interface RecurringTemplateRequest {
  name: string
  partnerId?: string | null
  currency: string
  exchangeRate?: number | null
  exchangeRateDate?: string | null
  intervalUnit: RecurringIntervalUnit
  intervalCount: number
  startOn: string
  endMode: RecurringEndMode
  endDate?: string | null
  maxOccurrences?: number | null
  autoFinalize: boolean
  autoSend: boolean
  status: RecurringStatus
  lines: RecurringTemplateLineRequest[]
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
    ...init,
  })
  if (!response.ok) {
    let body: unknown
    try {
      body = await response.json()
    } catch {
      body = await response.text().catch(() => undefined)
    }
    throw new ApiError(
      response.status,
      `Request failed: ${response.status}`,
      body,
    )
  }
  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export function listTemplates(): Promise<RecurringTemplate[]> {
  return request<RecurringTemplate[]>('/recurring-templates')
}

export function getTemplate(id: string): Promise<RecurringTemplate> {
  return request<RecurringTemplate>(`/recurring-templates/${id}`)
}

export function createTemplate(
  body: RecurringTemplateRequest,
): Promise<{ id: string }> {
  return request<{ id: string }>('/recurring-templates', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function updateTemplate(
  id: string,
  body: RecurringTemplateRequest,
): Promise<RecurringTemplate> {
  return request<RecurringTemplate>(`/recurring-templates/${id}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  })
}

export function activateTemplate(id: string): Promise<void> {
  return request<void>(`/recurring-templates/${id}/activate`, {
    method: 'POST',
  })
}

export function pauseTemplate(id: string): Promise<void> {
  return request<void>(`/recurring-templates/${id}/pause`, { method: 'POST' })
}

export function deleteTemplate(id: string): Promise<void> {
  return request<void>(`/recurring-templates/${id}`, { method: 'DELETE' })
}
