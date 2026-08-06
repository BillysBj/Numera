import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiRequest } from '@/lib/api'

// ASP.NET serializes these enums numerically. Keep the ordinals aligned with
// Numera.Modules.Banking and Numera.Modules.Banking.Reconciliation.
export const ConsentStatus = {
  Pending: 0,
  Active: 1,
  Expired: 2,
  Revoked: 3,
} as const
export type ConsentStatus = (typeof ConsentStatus)[keyof typeof ConsentStatus]

export const BankTransactionSource = {
  FinApi: 0,
  Csv: 1,
  Mt940: 2,
  Camt: 3,
} as const
export type BankTransactionSource =
  (typeof BankTransactionSource)[keyof typeof BankTransactionSource]

export const MatchStatus = {
  Unmatched: 0,
  Suggested: 1,
  Review: 2,
  Confirmed: 3,
  Ignored: 4,
} as const
export type MatchStatus = (typeof MatchStatus)[keyof typeof MatchStatus]

export const MatchTier = { High: 0, Review: 1 } as const
export type MatchTier = (typeof MatchTier)[keyof typeof MatchTier]

export interface BankWebFormResponse {
  webFormId: string
  redirectUrl: string
}

export interface BankAccountListItem {
  id: string
  iban: string
  displayName: string
  currency: string
  connectionId: string | null
  consentStatus: ConsentStatus | null
  lastSyncedAt: string | null
  consentExpiresAt: string | null
}

export interface BankConsentResponse {
  connectionId: string
  status: ConsentStatus
  expiresAt: string | null
}

export interface BankStatementImportResponse {
  bankAccountId: string
  insertedCount: number
}

export interface BankTransactionListItem {
  id: string
  bankAccountId: string
  source: BankTransactionSource
  amount: number
  valueDate: string
  bookingDate: string | null
  purpose: string | null
  counterpartyName: string | null
  counterpartyIban: string | null
  endToEndId: string | null
  matchStatus: MatchStatus
  confidenceScore: number | null
  matchedPaymentId: string | null
  createdAt: string
}

export interface BankTransactionListResponse {
  items: BankTransactionListItem[]
  page: number
  pageSize: number
  total: number
}

export interface MatchCandidate {
  openItemId: string
  documentNumber: string
  openAmount: number
  suggestedAllocation: number
  score: number
  tier: MatchTier
  reasons: string[]
}

export interface ConfirmAllocation {
  openItemId: string
  amount: number
}

export interface ConfirmBankTransactionRequest {
  allocations: ConfirmAllocation[]
  /** BankTransfer is numeric value 0. Omitted by default because the API fixes it. */
  method?: 0
}

export interface BankTransactionMatchResponse {
  transactionId: string
  matchStatus: MatchStatus
  paymentId: string | null
  alreadyConfirmed: boolean
}

export interface SyncBankAccountResponse {
  bankAccountId: string
  jobId: string
}

const ALL_MATCH_STATUSES = Object.values(MatchStatus) as MatchStatus[]

const bankingKeys = {
  all: ['banking'] as const,
  accounts: ['banking', 'accounts'] as const,
  consent: (connectionId: string) =>
    ['banking', 'consent', connectionId] as const,
  transactions: (status: MatchStatus | null) =>
    ['banking', 'transactions', status] as const,
  transaction: (id: string) => ['banking', 'transaction', id] as const,
  suggestions: (id: string) => ['banking', 'suggestions', id] as const,
}

function transactionListPath(
  status: MatchStatus | null,
  page = 1,
  pageSize = 25,
): string {
  const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
  // The endpoint's actual query parameter is `matchStatus`.
  if (status != null) query.set('matchStatus', String(status))
  return `/bank-transactions?${query.toString()}`
}

async function findTransactionById(id: string): Promise<BankTransactionListItem> {
  const encodedId = encodeURIComponent(id)

  async function findInStatus(status: MatchStatus) {
    let page = 1
    do {
      const response = await apiRequest<BankTransactionListResponse>(
        transactionListPath(status, page, 100),
      )
      const found = response.items.find((item) => item.id === encodedId || item.id === id)
      if (found) return found
      if (page * response.pageSize >= response.total) return null
      page += 1
    } while (true)
  }

  const matches = await Promise.all(ALL_MATCH_STATUSES.map(findInStatus))
  const found = matches.find((item) => item != null)
  if (!found) throw new Error('Bank transaction not found')
  return found
}

export function useBankAccounts() {
  return useQuery({
    queryKey: bankingKeys.accounts,
    queryFn: () => apiRequest<BankAccountListItem[]>('/bank-accounts'),
  })
}

export function useConnectBank() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () =>
      apiRequest<BankWebFormResponse>('/bank-accounts/connect', { method: 'POST' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: bankingKeys.accounts }),
  })
}

export function useReauthBankConnection() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (connectionId: string) =>
      apiRequest<BankWebFormResponse>(
        `/bank-accounts/${encodeURIComponent(connectionId)}/reauth`,
        { method: 'POST' },
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: bankingKeys.accounts }),
  })
}

export function useBankConsent(connectionId: string, enabled = true) {
  return useQuery({
    queryKey: bankingKeys.consent(connectionId),
    queryFn: () =>
      apiRequest<BankConsentResponse>(
        `/bank-accounts/connections/${encodeURIComponent(connectionId)}/consent`,
      ),
    enabled: Boolean(connectionId) && enabled,
  })
}

export function useSyncBankAccount() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (bankAccountId: string) =>
      apiRequest<SyncBankAccountResponse>(
        `/bank-accounts/${encodeURIComponent(bankAccountId)}/sync`,
        { method: 'POST' },
      ),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: bankingKeys.accounts }),
        queryClient.invalidateQueries({ queryKey: ['banking', 'transactions'] }),
      ])
    },
  })
}

export function useImportBankStatement() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ file, bankAccountId }: { file: File; bankAccountId?: string }) => {
      const form = new FormData()
      form.append('file', file)
      const query = bankAccountId
        ? `?bankAccountId=${encodeURIComponent(bankAccountId)}`
        : ''
      return apiRequest<BankStatementImportResponse>(`/bank-accounts/import${query}`, {
        method: 'POST',
        body: form,
      })
    },
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: bankingKeys.accounts }),
        queryClient.invalidateQueries({ queryKey: ['banking', 'transactions'] }),
      ])
    },
  })
}

export function useBankTransactions(status: MatchStatus | null) {
  return useQuery({
    queryKey: bankingKeys.transactions(status),
    queryFn: () =>
      apiRequest<BankTransactionListResponse>(transactionListPath(status)),
  })
}

export function useBankTransaction(id: string) {
  return useQuery({
    queryKey: bankingKeys.transaction(id),
    queryFn: () => findTransactionById(id),
    enabled: Boolean(id),
  })
}

export function useMatchSuggestions(id: string, enabled = true) {
  return useQuery({
    queryKey: bankingKeys.suggestions(id),
    queryFn: () =>
      apiRequest<MatchCandidate[]>(
        `/bank-transactions/${encodeURIComponent(id)}/suggestions`,
      ),
    enabled: Boolean(id) && enabled,
  })
}

function useMatchCommand(
  command: 'confirm' | 'unmatch' | 'ignore',
  id?: string,
) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: string | ConfirmBankTransactionRequest) => {
      const transactionId = id ?? (input as string)
      const body = command === 'confirm' ? JSON.stringify(input) : undefined
      return apiRequest<BankTransactionMatchResponse>(
        `/bank-transactions/${encodeURIComponent(transactionId)}/${command}`,
        { method: 'POST', body },
      )
    },
    onSuccess: async (_, input) => {
      const transactionId = id ?? (input as string)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['banking', 'transactions'] }),
        queryClient.invalidateQueries({ queryKey: bankingKeys.transaction(transactionId) }),
        queryClient.invalidateQueries({ queryKey: ['open-items'] }),
      ])
    },
  })
}

export function useConfirmBankTransaction(id: string) {
  return useMatchCommand('confirm', id)
}

export function useUnmatchBankTransaction(id: string) {
  return useMatchCommand('unmatch', id)
}

export function useIgnoreBankTransaction() {
  return useMatchCommand('ignore')
}
