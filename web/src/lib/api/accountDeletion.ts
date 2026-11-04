import { apiRequest } from '../api'

/**
 * Account-deletion flow client (Phase B1). The status/request/cancel endpoints are Owner-only
 * (`/api/account/deletion`) and only move the tenant in/out of the read-only grace window; the
 * irreversible purge runs later via the server-side sweep job.
 */
export interface AccountDeletionStatus {
  pendingDeletion: boolean
  requestedAt: string | null
  scheduledFor: string | null
  graceDays: number
  lastExportAt: string | null
  hasFreshExport: boolean
}

export const getAccountDeletionStatus = () =>
  apiRequest<AccountDeletionStatus>('/account/deletion')

export const requestAccountDeletion = (confirmationName: string) =>
  apiRequest<AccountDeletionStatus>('/account/deletion/request', {
    method: 'POST',
    body: JSON.stringify({ confirmationName }),
  })

export const cancelAccountDeletion = () =>
  apiRequest<AccountDeletionStatus>('/account/deletion/cancel', { method: 'POST' })
