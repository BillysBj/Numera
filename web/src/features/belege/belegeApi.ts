import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { API_BASE, apiBlob, apiRequest } from '@/lib/api'

// ASP.NET serializes both enums numerically. Keep these ordinals aligned with
// Numera.Modules.Sales.Belege.ReceiptStatus / ReceiptSource.
export const ReceiptStatus = {
  Captured: 0,
  Extracted: 1,
  Reviewed: 2,
  Booked: 3,
  Duplicate: 4,
  Rejected: 5,
  Quarantined: 6,
} as const
export type ReceiptStatus = (typeof ReceiptStatus)[keyof typeof ReceiptStatus]

export const ReceiptSource = {
  Camera: 0,
  Upload: 1,
  Email: 2,
  EInvoice: 3,
} as const
export type ReceiptSource = (typeof ReceiptSource)[keyof typeof ReceiptSource]

export const PostingDirection = { Debit: 1, Credit: 2 } as const
export type PostingDirection =
  (typeof PostingDirection)[keyof typeof PostingDirection]

// Keep aligned with Numera.Modules.Sales.Belege.ReceiptPaymentStatus (numeric wire).
export const ReceiptPaymentStatus = {
  Unpaid: 0,
  PartiallyPaid: 1,
  Paid: 2,
} as const
export type ReceiptPaymentStatus =
  (typeof ReceiptPaymentStatus)[keyof typeof ReceiptPaymentStatus]

// Supplier-payment methods mirror Numera.Modules.Sales.Payments.PaymentMethod.
export const SupplierPaymentMethod = {
  BankTransfer: 0,
  Cash: 1,
  Card: 2,
  Sepa: 3,
  Other: 4,
} as const
export type SupplierPaymentMethod =
  (typeof SupplierPaymentMethod)[keyof typeof SupplierPaymentMethod]

export interface ReceiptFieldConfidence {
  supplierName?: number | null
  supplierVatId?: number | null
  invoiceNumber?: number | null
  invoiceDate?: number | null
  netAmount?: number | null
  vatAmount?: number | null
  grossAmount?: number | null
  vatRatePercent?: number | null
}

export interface ReceiptListItem {
  id: string
  source: ReceiptSource
  status: ReceiptStatus
  supplierName: string | null
  supplierVatId: string | null
  invoiceNumber: string | null
  invoiceDate: string | null
  netAmount: number | null
  vatAmount: number | null
  grossAmount: number | null
  vatRatePercent: number | null
  currency: string | null
  fieldConfidence: ReceiptFieldConfidence | null
  matchedPartnerId: string | null
  originalFileName: string | null
  createdAt: string
}

export interface ReceiptListResponse {
  items: ReceiptListItem[]
  page: number
  pageSize: number
  total: number
}

export interface ReceiptDetail extends ReceiptListItem {
  inboundDocumentId: string | null
  archiveId: string | null
  contentHash: string
  expenseDate: string | null
  expenseAccountOverride: string | null
  journalEntryId: string | null
  originalContentType: string | null
  byteSize: number | null
  receivedAt: string | null
  uploadedByUserId: string | null
  openAmount: number | null
  paymentStatus: ReceiptPaymentStatus | null
}

export interface RecordSupplierPaymentRequest {
  amount: number
  valueDate: string
  method: SupplierPaymentMethod
  reference?: string
}

export interface SupplierPaymentListItem {
  id: string
  amount: number
  valueDate: string
  method: SupplierPaymentMethod
  reference: string | null
  reversesPaymentId: string | null
  recordedAt: string
}

export interface SupplierPaymentListResponse {
  items: SupplierPaymentListItem[]
}

export interface ReviewReceiptRequest {
  supplierPartnerId: string | null
  expenseAccountOverride: string | null
  vatRatePercent: number | null
  netAmount: number
  vatAmount: number
  grossAmount: number
  invoiceNumber: string | null
  invoiceDate: string | null
  expenseDate: string | null
}

export interface ReviewReceiptResponse {
  id: string
  status: ReceiptStatus
  reviewedByUserId: string
  reviewedAt: string
}

export interface ReceiptProposalSupplier {
  partnerId: string | null
  name: string | null
  creditorAccount: string
}

export interface ReceiptProposalBreakdown {
  vatRatePercent: number
  netAmount: number
  vatAmount: number
}

export interface ReceiptProposalPostingLeg {
  accountNumber: string
  direction: PostingDirection
  amount: number
  vatRatePercent: number | null
  taxKey: number | null
}

export interface ReceiptBookingProposal {
  receiptId: string
  supplier: ReceiptProposalSupplier
  expenseAccount: string
  entryDate: string
  breakdowns: ReceiptProposalBreakdown[]
  postingLegs: ReceiptProposalPostingLeg[]
  totalNet: number
  totalVat: number
  totalGross: number
}

export interface ReceiptBookingResponse {
  receiptId: string
  journalEntryId: string
  status: ReceiptStatus
  alreadyBooked: boolean
}

export interface ReceiptUploadResponse {
  id: string
  status: ReceiptStatus
}

export interface ReceiptMailbox {
  address: string
  isActive: boolean
  createdAt: string
}

const receiptKeys = {
  all: ['receipts'] as const,
  list: (status: ReceiptStatus | null) => ['receipts', 'list', status] as const,
  detail: (id: string) => ['receipts', 'detail', id] as const,
  proposal: (id: string) => ['receipts', 'proposal', id] as const,
  original: (id: string) => ['receipts', 'original', id] as const,
  payments: (id: string) => ['receipts', 'payments', id] as const,
  mailbox: ['receipts', 'mailbox'] as const,
}

export function useReceipts(status: ReceiptStatus | null) {
  const query = status == null ? '' : `?status=${status}`
  return useQuery({
    queryKey: receiptKeys.list(status),
    queryFn: () => apiRequest<ReceiptListResponse>(`/receipts${query}`),
  })
}

export function useReceipt(id: string) {
  return useQuery({
    queryKey: receiptKeys.detail(id),
    queryFn: () => apiRequest<ReceiptDetail>(`/receipts/${encodeURIComponent(id)}`),
    enabled: Boolean(id),
  })
}

export function useReceiptProposal(id: string, enabled: boolean) {
  return useQuery({
    queryKey: receiptKeys.proposal(id),
    queryFn: () =>
      apiRequest<ReceiptBookingProposal>(
        `/receipts/${encodeURIComponent(id)}/proposal`,
      ),
    enabled: Boolean(id) && enabled,
    retry: false,
  })
}

export function useReviewReceipt(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: ReviewReceiptRequest) =>
      apiRequest<ReviewReceiptResponse>(
        `/receipts/${encodeURIComponent(id)}/review`,
        { method: 'PATCH', body: JSON.stringify(body) },
      ),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: receiptKeys.all }),
        queryClient.invalidateQueries({ queryKey: receiptKeys.detail(id) }),
        queryClient.invalidateQueries({ queryKey: receiptKeys.proposal(id) }),
      ])
    },
  })
}

export function useConfirmBookReceipt(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () =>
      apiRequest<ReceiptBookingResponse>(
        `/receipts/${encodeURIComponent(id)}/confirm-book`,
        { method: 'POST' },
      ),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: receiptKeys.all }),
        queryClient.invalidateQueries({ queryKey: receiptKeys.detail(id) }),
        queryClient.removeQueries({ queryKey: receiptKeys.proposal(id) }),
      ])
    },
  })
}

export function useUploadReceipt() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return apiRequest<ReceiptUploadResponse>('/receipts', {
        method: 'POST',
        body: form,
      })
    },
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: receiptKeys.all }),
  })
}

export function receiptOriginalUrl(id: string): string {
  return `${API_BASE}/receipts/${encodeURIComponent(id)}/original`
}

export function useReceiptOriginal(id: string, enabled: boolean) {
  return useQuery({
    queryKey: receiptKeys.original(id),
    queryFn: () => apiBlob(`/receipts/${encodeURIComponent(id)}/original`),
    enabled: Boolean(id) && enabled,
    staleTime: Number.POSITIVE_INFINITY,
  })
}

export function useReceiptMailbox() {
  return useQuery({
    queryKey: receiptKeys.mailbox,
    queryFn: () => apiRequest<ReceiptMailbox>('/receipts/mailbox'),
  })
}

// Supplier payments (Abflussprinzip): recording one makes the EÜR recognize the
// expense on the payment's value date. Mirrors the sales-side payment flow.
export function useSupplierPayments(id: string, enabled: boolean) {
  return useQuery({
    queryKey: receiptKeys.payments(id),
    queryFn: () =>
      apiRequest<SupplierPaymentListResponse>(
        `/receipts/${encodeURIComponent(id)}/payments`,
      ),
    enabled: Boolean(id) && enabled,
  })
}

function invalidateReceiptPayments(
  queryClient: ReturnType<typeof useQueryClient>,
  id: string,
) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: receiptKeys.detail(id) }),
    queryClient.invalidateQueries({ queryKey: receiptKeys.payments(id) }),
    queryClient.invalidateQueries({ queryKey: receiptKeys.all }),
  ])
}

export function useRecordSupplierPayment(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: RecordSupplierPaymentRequest) =>
      apiRequest<{ id: string }>(
        `/receipts/${encodeURIComponent(id)}/payments`,
        { method: 'POST', body: JSON.stringify(body) },
      ),
    onSuccess: () => invalidateReceiptPayments(queryClient, id),
  })
}

export function useReverseSupplierPayment(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (paymentId: string) =>
      apiRequest<{ id: string }>(
        `/receipts/${encodeURIComponent(id)}/payments/${encodeURIComponent(paymentId)}/reverse`,
        { method: 'POST' },
      ),
    onSuccess: () => invalidateReceiptPayments(queryClient, id),
  })
}
