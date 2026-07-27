import { API_BASE, ApiError } from '../api'

export { ApiError }

export interface DunningLevelConfigDto {
  level: number
  name: string
  daysAfterDue: number
  fee: number
  chargeInterest: boolean
  interestRatePercent: number
  templateTextDe: string
  templateTextEn: string
}

export interface DunningRunResult {
  issued: number
  skipped: number
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
    ...init,
  })

  if (!res.ok) {
    let body: unknown
    try {
      body = await res.json()
    } catch {
      body = await res.text().catch(() => undefined)
    }
    throw new ApiError(res.status, `Request failed: ${res.status}`, body)
  }

  if (res.status === 204) return undefined as T
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export async function getConfig(): Promise<DunningLevelConfigDto[]> {
  const response = await request<{ levels: DunningLevelConfigDto[] }>(
    '/dunning/config',
  )
  return response.levels
}

export async function saveConfig(
  levels: DunningLevelConfigDto[],
): Promise<DunningLevelConfigDto[]> {
  const response = await request<{ levels: DunningLevelConfigDto[] }>(
    '/dunning/config',
    { method: 'PUT', body: JSON.stringify({ levels }) },
  )
  return response.levels
}

export function runDunning(): Promise<DunningRunResult> {
  return request<DunningRunResult>('/dunning/run', { method: 'POST' })
}
