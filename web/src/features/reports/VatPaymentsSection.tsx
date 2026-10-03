import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Dialog, DialogFooter } from '@/components/ui/dialog'
import RecordVatPaymentDialog from './RecordVatPaymentDialog'
import { VatPaymentKind, useReverseVatPayment, useVatPayments } from './vatPaymentsApi'

export default function VatPaymentsSection() {
  const { t, i18n } = useTranslation('reports')
  const payments = useVatPayments()
  const reverse = useReverseVatPayment()
  const [recordOpen, setRecordOpen] = useState(false)
  const [reverseId, setReverseId] = useState<string | null>(null)
  const reversed = new Set(payments.data?.items.map((item) => item.reversesPaymentId))
  const money = new Intl.NumberFormat(i18n.language, { style: 'currency', currency: 'EUR' })

  return (
    <section className="mt-6 rounded-xl border border-border bg-card p-4" aria-labelledby="vat-payments-title">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="vat-payments-title" className="text-lg font-semibold">{t('vatPayments.title')}</h2>
        <Button onClick={() => setRecordOpen(true)}>{t('vatPayments.record')}</Button>
      </div>
      <p className="mt-2 text-sm text-muted-foreground">{t('vatPayments.description')}</p>
      {payments.isLoading && <p className="mt-3 text-sm">{t('loading')}</p>}
      {payments.isError && <p role="alert" className="mt-3 text-sm text-destructive">{t('errors.load')}</p>}
      {payments.data && (payments.data.items.length === 0
        ? <p className="mt-3 text-sm text-muted-foreground">{t('vatPayments.empty')}</p>
        : <div className="mt-4 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead><tr>
              {['valueDate', 'kind', 'amount', 'reference', 'status'].map((key) => (
                <th key={key} scope="col" className="px-3 py-2">{t(`vatPayments.${key}`)}</th>
              ))}
            </tr></thead>
            <tbody>{payments.data.items.map((payment) => (
              <tr key={payment.id} className="border-t border-border">
                <td className="whitespace-nowrap px-3 py-2">{new Date(`${payment.valueDate}T00:00:00`).toLocaleDateString(i18n.language)}</td>
                <td className="px-3 py-2">{t(payment.kind === VatPaymentKind.Payment ? 'vatPayments.payment' : 'vatPayments.refund')}</td>
                <td className="whitespace-nowrap px-3 py-2 tabular-nums">{money.format(payment.amount)}</td>
                <td className="px-3 py-2">{payment.reference ?? '—'}</td>
                <td className="px-3 py-2">
                  {payment.reversesPaymentId ? t('vatPayments.reversal') : reversed.has(payment.id)
                    ? t('vatPayments.reversed')
                    : <Button variant="outline" onClick={() => { reverse.reset(); setReverseId(payment.id) }}>
                      {t('vatPayments.reverse')}
                    </Button>}
                </td>
              </tr>
            ))}</tbody>
          </table>
        </div>)}
      <RecordVatPaymentDialog open={recordOpen} onOpenChange={setRecordOpen} />
      <Dialog open={reverseId !== null} onOpenChange={(open) => { if (!open && !reverse.isPending) setReverseId(null) }}
        title={t('vatPayments.reverse')} description={t('vatPayments.reverseDescription')}>
        {reverse.isError && <p role="alert" className="text-sm text-destructive">{t('vatPayments.error')}</p>}
        <DialogFooter>
          <Button variant="ghost" disabled={reverse.isPending} onClick={() => setReverseId(null)}>{t('vatPayments.cancel')}</Button>
          <Button disabled={reverse.isPending} onClick={async () => {
            if (!reverseId) return
            try { await reverse.mutateAsync(reverseId); setReverseId(null) } catch { /* mutation renders the error */ }
          }}>{t('vatPayments.reverse')}</Button>
        </DialogFooter>
      </Dialog>
    </section>
  )
}
