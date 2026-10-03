import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useForm } from 'react-hook-form'
import { ApiError } from '@/lib/api'
import { Dialog, DialogFooter } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { Button } from '@/components/ui/button'
import { VatPaymentKind, useRecordVatPayment, type RecordVatPaymentRequest } from './vatPaymentsApi'

function defaults(): Omit<RecordVatPaymentRequest, 'amount'> {
  const now = new Date()
  return {
    kind: VatPaymentKind.Payment,
    valueDate: new Date(now.getTime() - now.getTimezoneOffset() * 60_000).toISOString().slice(0, 10),
    reference: '',
  }
}

export default function RecordVatPaymentDialog({ open, onOpenChange }: {
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const { t } = useTranslation('reports')
  const mutation = useRecordVatPayment()
  const [notice, setNotice] = useState<string | null>(null)
  const { register, handleSubmit, reset, setError, formState: { errors } } = useForm<RecordVatPaymentRequest>({
    defaultValues: defaults(),
  })
  useEffect(() => {
    if (open) {
      reset(defaults())
      setNotice(null)
    }
  }, [open, reset])

  const submit = handleSubmit(async (values) => {
    setNotice(null)
    try {
      await mutation.mutateAsync({ ...values, reference: values.reference?.trim() || undefined })
      onOpenChange(false)
    } catch (error) {
      if (error instanceof ApiError && error.status === 422) {
        const fields = (error.body as { errors?: Record<string, string[]> })?.errors
        for (const [key, messages] of Object.entries(fields ?? {})) {
          const field = key.toLowerCase()
          const name = field === 'valuedate' ? 'valueDate' : field
          if (name === 'amount' || name === 'kind' || name === 'valueDate' || name === 'reference') {
            setError(name, { message: messages[0] })
          }
        }
      }
      setNotice(t('vatPayments.error'))
    }
  })

  return (
    <Dialog open={open} onOpenChange={(value) => { if (!mutation.isPending) onOpenChange(value) }}
      title={t('vatPayments.record')} description={t('vatPayments.description')}>
      <form onSubmit={submit} noValidate>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <label className="flex flex-col gap-1.5 text-sm font-medium">
            {t('vatPayments.amount')}
            <Input type="number" min="0.01" step="0.01" autoFocus aria-invalid={!!errors.amount}
              aria-describedby={errors.amount ? 'vat-payment-amount-error' : undefined}
              {...register('amount', {
                valueAsNumber: true,
                validate: (value) => (Number.isFinite(value) && value > 0) || t('vatPayments.amountPositive'),
              })} />
            {errors.amount && <span id="vat-payment-amount-error" className="text-destructive">{errors.amount.message}</span>}
          </label>
          <label className="flex flex-col gap-1.5 text-sm font-medium">
            {t('vatPayments.valueDate')}
            <Input type="date" aria-invalid={!!errors.valueDate}
              {...register('valueDate', { required: t('vatPayments.dateRequired') })} />
            {errors.valueDate && <span className="text-destructive">{errors.valueDate.message}</span>}
          </label>
          <label className="flex flex-col gap-1.5 text-sm font-medium">
            {t('vatPayments.kind')}
            <Select {...register('kind', { setValueAs: Number })}>
              <option value={VatPaymentKind.Payment}>{t('vatPayments.payment')}</option>
              <option value={VatPaymentKind.Refund}>{t('vatPayments.refund')}</option>
            </Select>
            {errors.kind && <span className="text-destructive">{errors.kind.message}</span>}
          </label>
          <label className="flex flex-col gap-1.5 text-sm font-medium">
            {t('vatPayments.reference')}
            <Input {...register('reference')} />
            {errors.reference && <span className="text-destructive">{errors.reference.message}</span>}
          </label>
        </div>
        {notice && <p role="alert" className="mt-3 text-sm text-destructive">{notice}</p>}
        <DialogFooter>
          <Button type="button" variant="ghost" disabled={mutation.isPending} onClick={() => onOpenChange(false)}>
            {t('vatPayments.cancel')}
          </Button>
          <Button type="submit" disabled={mutation.isPending}>
            {t(mutation.isPending ? 'vatPayments.saving' : 'vatPayments.record')}
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  )
}
