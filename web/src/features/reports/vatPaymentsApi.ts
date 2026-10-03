import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiRequest } from '@/lib/api'

export const VatPaymentKind = { Payment: 0, Refund: 1 } as const
export type VatPaymentKind = (typeof VatPaymentKind)[keyof typeof VatPaymentKind]
export interface RecordVatPaymentRequest {
  amount: number
  kind: VatPaymentKind
  valueDate: string
  reference?: string
}
export interface VatPayment extends Omit<RecordVatPaymentRequest, 'reference'> {
  id: string
  reference: string | null
  reversesPaymentId: string | null
  recordedAt: string
}

export function useVatPayments() {
  return useQuery({
    queryKey: ['vat-payments'],
    queryFn: () => apiRequest<{ items: VatPayment[] }>('/vat-payments'),
  })
}

function useInvalidateVatPayments() {
  const client = useQueryClient()
  return () => Promise.all([
    client.invalidateQueries({ queryKey: ['vat-payments'] }),
    client.invalidateQueries({ queryKey: ['reports', 'euer'] }),
  ])
}

export function useRecordVatPayment() {
  const invalidate = useInvalidateVatPayments()
  return useMutation({
    mutationFn: (body: RecordVatPaymentRequest) => apiRequest<{ id: string }>(
      '/vat-payments', { method: 'POST', body: JSON.stringify(body) },
    ),
    onSuccess: invalidate,
  })
}

export function useReverseVatPayment() {
  const invalidate = useInvalidateVatPayments()
  return useMutation({
    mutationFn: (id: string) => apiRequest<{ id: string }>(
      `/vat-payments/${encodeURIComponent(id)}/reverse`, { method: 'POST' },
    ),
    onSuccess: invalidate,
  })
}
