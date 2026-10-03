import { apiRequest } from '../api'

export interface EmailSettingsDto {
  host: string | null
  port: number
  useSsl: boolean
  username: string | null
  hasPassword: boolean
  fromAddress: string | null
  fromName: string | null
  invoiceSubject: string | null
  invoiceBody: string | null
  dunningSubject: string | null
  dunningBody: string | null
}

export type UpdateEmailSettingsRequest = Omit<EmailSettingsDto, 'hasPassword'> & { password?: string }
export interface TestEmailResult { success: boolean; error: string | null }

export const getEmailSettings = () => apiRequest<EmailSettingsDto>('/settings/email')
export const saveEmailSettings = (body: UpdateEmailSettingsRequest) =>
  apiRequest<EmailSettingsDto>('/settings/email', { method: 'PUT', body: JSON.stringify(body) })
export const sendTestEmail = (toAddress: string) =>
  apiRequest<TestEmailResult>('/settings/email/test', {
    method: 'POST', body: JSON.stringify({ toAddress: toAddress || null }),
  })
