import { apiRequest } from '@/lib/api'

/** Shared receipt-mailbox settings as returned by the BFF (never includes the password). */
export interface BelegeMailboxSettingsDto {
  host: string
  port: number
  useSsl: boolean
  username: string
  domain: string
  isActive: boolean
  hasPassword: boolean
  updatedAt: string | null
}

/** Payload to save the shared receipt-mailbox settings. Omit `password` to keep the stored one. */
export interface UpdateBelegeMailboxRequest {
  host: string
  port: number
  useSsl: boolean
  username: string
  password?: string
  domain: string
  isActive: boolean
}

/** `GET /api/settings/belege-mailbox` (Owner-only). */
export function getBelegeMailboxSettings(): Promise<BelegeMailboxSettingsDto> {
  return apiRequest<BelegeMailboxSettingsDto>('/settings/belege-mailbox')
}

/** `PUT /api/settings/belege-mailbox` (Owner-only). */
export function saveBelegeMailboxSettings(
  body: UpdateBelegeMailboxRequest,
): Promise<BelegeMailboxSettingsDto> {
  return apiRequest<BelegeMailboxSettingsDto>('/settings/belege-mailbox', {
    method: 'PUT',
    body: JSON.stringify(body),
  })
}
