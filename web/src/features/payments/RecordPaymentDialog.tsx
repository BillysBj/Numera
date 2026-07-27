import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useForm, type Resolver } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  ApiError,
  PaymentMethod,
  recordPayment,
} from '@/lib/api/payments'
import { makePaymentSchema, type PaymentFormValues } from './paymentSchema'
import { Dialog, DialogFooter } from '@/components/ui/dialog'
import { Form } from '@/components/ui/form'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { Button } from '@/components/ui/button'

export interface PaymentOpenItem {
  id: string
  documentNumber: string
  openAmount: number
  currency: string
}

interface RecordPaymentDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  openItem: PaymentOpenItem | null
}

const METHOD_KEYS = Object.keys(PaymentMethod) as (keyof typeof PaymentMethod)[]

function today(): string {
  const date = new Date()
  const offset = date.getTimezoneOffset()
  return new Date(date.getTime() - offset * 60_000).toISOString().slice(0, 10)
}

function validationErrors(error: ApiError): Record<string, string> | null {
  if (error.status !== 422) return null
  const body = error.body as { errors?: Record<string, string[]> } | undefined
  if (!body?.errors) return null
  return Object.fromEntries(
    Object.entries(body.errors)
      .filter(([, messages]) => messages.length > 0)
      .map(([field, messages]) => [field, messages[0]]),
  )
}

export default function RecordPaymentDialog({
  open,
  onOpenChange,
  openItem,
}: RecordPaymentDialogProps) {
  const { t } = useTranslation('payments')
  const queryClient = useQueryClient()
  const [notice, setNotice] = useState<string | null>(null)
  const schema = useMemo(
    () => makePaymentSchema(openItem?.openAmount ?? 0, t),
    [openItem?.openAmount, t],
  )
  const form = useForm<PaymentFormValues>({
    resolver: zodResolver(schema) as Resolver<PaymentFormValues>,
    defaultValues: {
      amount: openItem?.openAmount ?? 0,
      valueDate: today(),
      method: PaymentMethod.BankTransfer,
      reference: undefined,
    },
  })
  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = form

  useEffect(() => {
    if (open && openItem) {
      setNotice(null)
      reset({
        amount: openItem.openAmount,
        valueDate: today(),
        method: PaymentMethod.BankTransfer,
        reference: undefined,
      })
    }
  }, [open, openItem, reset])

  const mutation = useMutation({
    mutationFn: (values: PaymentFormValues) => {
      if (!openItem) throw new Error('No open item selected')
      return recordPayment({
        amount: values.amount,
        valueDate: values.valueDate,
        method: values.method,
        reference: values.reference,
        allocations: [{ openItemId: openItem.id, amount: values.amount }],
      })
    },
  })

  const onSubmit = handleSubmit(async (values) => {
    setNotice(null)
    try {
      await mutation.mutateAsync(values)
      await queryClient.invalidateQueries({ queryKey: ['open-items'] })
      onOpenChange(false)
    } catch (error) {
      if (error instanceof ApiError) {
        const fields = validationErrors(error)
        if (fields) {
          let mapped = false
          for (const [field, message] of Object.entries(fields)) {
            const normalized = field.toLowerCase()
            if (normalized === 'amount' || normalized.includes('allocations')) {
              setError('amount', { message })
              mapped = true
            } else if (normalized === 'valuedate') {
              setError('valueDate', { message })
              mapped = true
            } else if (normalized === 'method') {
              setError('method', { message })
              mapped = true
            } else if (normalized === 'reference') {
              setError('reference', { message })
              mapped = true
            }
          }
          setNotice(mapped ? t('serverValidationError') : t('error'))
          return
        }
      }
      setNotice(t('error'))
    }
  })

  if (!openItem) return null

  const amount = new Intl.NumberFormat('de-DE', {
    style: 'currency',
    currency: openItem.currency,
  }).format(openItem.openAmount)

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={t('title')}
      description={t('description', {
        documentNumber: openItem.documentNumber,
        openAmount: amount,
      })}
    >
      <Form {...form}>
        <form onSubmit={onSubmit}>
          <div className="grid grid-cols-2 gap-3">
            <Field label={t('fields.amount')} error={errors.amount?.message}>
              <Input
                type="number"
                min="0.01"
                step="0.01"
                {...register('amount', { valueAsNumber: true })}
              />
            </Field>
            <Field
              label={t('fields.valueDate')}
              error={errors.valueDate?.message}
            >
              <Input type="date" {...register('valueDate')} />
            </Field>
            <Field label={t('fields.method')} error={errors.method?.message}>
              <Select
                {...register('method', { setValueAs: (value) => Number(value) })}
              >
                {METHOD_KEYS.map((key) => (
                  <option key={key} value={PaymentMethod[key]}>
                    {t(`methods.${key}`)}
                  </option>
                ))}
              </Select>
            </Field>
            <Field
              label={t('fields.reference')}
              error={errors.reference?.message}
            >
              <Input {...register('reference')} />
            </Field>
          </div>
          {notice && (
            <p role="alert" className="mt-3 text-sm text-destructive">
              {notice}
            </p>
          )}
          <DialogFooter>
            <Button
              variant="ghost"
              onClick={() => onOpenChange(false)}
              disabled={mutation.isPending}
            >
              {t('actions.cancel')}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending
                ? t('actions.saving')
                : t('actions.record')}
            </Button>
          </DialogFooter>
        </form>
      </Form>
    </Dialog>
  )
}

function Field({
  label,
  error,
  children,
}: {
  label: string
  error?: string
  children: React.ReactNode
}) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-sm font-medium leading-none">{label}</span>
      {children}
      {error && <span className="text-sm text-destructive">{error}</span>}
    </label>
  )
}
