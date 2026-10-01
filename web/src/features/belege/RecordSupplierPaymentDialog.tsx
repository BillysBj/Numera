import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useForm, type Resolver } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { ApiError } from '@/lib/api'
import { SupplierPaymentMethod, useRecordSupplierPayment } from './belegeApi'
import {
  makeSupplierPaymentSchema,
  type SupplierPaymentFormValues,
} from './supplierPaymentSchema'
import { Dialog, DialogFooter } from '@/components/ui/dialog'
import { Form } from '@/components/ui/form'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { Button } from '@/components/ui/button'

interface RecordSupplierPaymentDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  receiptId: string
  openAmount: number
  currency: string
  supplierName: string | null
}

const METHOD_KEYS = Object.keys(
  SupplierPaymentMethod,
) as (keyof typeof SupplierPaymentMethod)[]

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

export default function RecordSupplierPaymentDialog({
  open,
  onOpenChange,
  receiptId,
  openAmount,
  currency,
  supplierName,
}: RecordSupplierPaymentDialogProps) {
  const { t } = useTranslation('belege')
  const [notice, setNotice] = useState<string | null>(null)
  const mutation = useRecordSupplierPayment(receiptId)
  const schema = useMemo(
    () => makeSupplierPaymentSchema(openAmount, t),
    [openAmount, t],
  )
  const form = useForm<SupplierPaymentFormValues>({
    resolver: zodResolver(schema) as Resolver<SupplierPaymentFormValues>,
    defaultValues: {
      amount: openAmount,
      valueDate: today(),
      method: SupplierPaymentMethod.BankTransfer,
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
    if (open) {
      setNotice(null)
      reset({
        amount: openAmount,
        valueDate: today(),
        method: SupplierPaymentMethod.BankTransfer,
        reference: undefined,
      })
    }
  }, [open, openAmount, reset])

  const onSubmit = handleSubmit(async (values) => {
    setNotice(null)
    try {
      await mutation.mutateAsync({
        amount: values.amount,
        valueDate: values.valueDate,
        method: values.method,
        reference: values.reference,
      })
      onOpenChange(false)
    } catch (error) {
      if (error instanceof ApiError) {
        const fields = validationErrors(error)
        if (fields) {
          let mapped = false
          for (const [field, message] of Object.entries(fields)) {
            const normalized = field.toLowerCase()
            if (normalized === 'amount' || normalized === 'payment') {
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
          setNotice(mapped ? t('payments.serverValidationError') : t('payments.error'))
          return
        }
      }
      setNotice(t('payments.error'))
    }
  })

  const amount = new Intl.NumberFormat('de-DE', {
    style: 'currency',
    currency,
  }).format(openAmount)

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={t('payments.dialog.title')}
      description={t('payments.dialog.description', {
        supplier: supplierName ?? t('payments.unknownSupplier'),
        openAmount: amount,
      })}
    >
      <Form {...form}>
        <form onSubmit={onSubmit}>
          <div className="grid grid-cols-2 gap-3">
            <Field label={t('payments.fields.amount')} error={errors.amount?.message}>
              <Input
                type="number"
                min="0.01"
                step="0.01"
                {...register('amount', { valueAsNumber: true })}
              />
            </Field>
            <Field
              label={t('payments.fields.valueDate')}
              error={errors.valueDate?.message}
            >
              <Input type="date" {...register('valueDate')} />
            </Field>
            <Field label={t('payments.fields.method')} error={errors.method?.message}>
              <Select
                {...register('method', { setValueAs: (value) => Number(value) })}
              >
                {METHOD_KEYS.map((key) => (
                  <option key={key} value={SupplierPaymentMethod[key]}>
                    {t(`payments.methods.${key}`)}
                  </option>
                ))}
              </Select>
            </Field>
            <Field
              label={t('payments.fields.reference')}
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
              {t('payments.actions.cancel')}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending
                ? t('payments.actions.saving')
                : t('payments.actions.record')}
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
