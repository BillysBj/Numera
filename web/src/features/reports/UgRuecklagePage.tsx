import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { apiRequest, getMe } from '@/lib/api'
import { useUgRuecklage } from './ustvaApi'

const moneyFormat = new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' })

export default function UgRuecklagePage() {
  const { t } = useTranslation('reports')
  const me = useQuery({ queryKey: ['me'], queryFn: getMe })
  const isOwner = me.data?.role === 'Owner'
  const queryClient = useQueryClient()
  const currentYear = new Date().getFullYear()
  const [jahr, setJahr] = useState(currentYear - 1)
  const [verlustvortrag, setVerlustvortrag] = useState('0')
  const [bookMessage, setBookMessage] = useState<string | null>(null)

  const verlustvortragNum = Number(verlustvortrag || 0)
  const report = useUgRuecklage(jahr, verlustvortragNum)

  const book = useMutation({
    mutationFn: () =>
      apiRequest(`/reports/ug-ruecklage/${jahr}/buchen`, {
        method: 'POST',
        body: JSON.stringify({ verlustvortrag: verlustvortragNum }),
      }),
    onSuccess: () => {
      setBookMessage(t('ugRuecklage.booked'))
      void queryClient.invalidateQueries({ queryKey: ['reports'] })
    },
    onError: () => setBookMessage(t('ugRuecklage.bookError')),
  })

  const canBook =
    isOwner && report.data != null && report.data.hinweis == null && report.data.ruecklage > 0

  return (
    <main className="app-main" style={{ maxWidth: '760px' }}>
      <div className="mb-6">
        <h1 className="text-2xl font-semibold">{t('ugRuecklage.title')}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{t('ugRuecklage.subtitle')}</p>
      </div>

      <section className="mb-5 flex flex-wrap items-end gap-3 rounded-xl border border-border bg-card p-4">
        <label className="flex flex-col gap-1.5 text-sm font-medium">
          {t('filters.year')}
          <Input
            className="w-28"
            type="number"
            min={2000}
            max={2100}
            value={jahr}
            onChange={(event) => { setJahr(Number(event.target.value)); setBookMessage(null) }}
          />
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-medium">
          {t('ugRuecklage.lossCarryforward')}
          <Input
            className="w-40"
            type="number"
            step="0.01"
            min={0}
            value={verlustvortrag}
            onChange={(event) => { setVerlustvortrag(event.target.value); setBookMessage(null) }}
          />
        </label>
      </section>

      {report.isLoading && <p className="text-sm text-muted-foreground">{t('loading')}</p>}
      {report.isError && <p role="alert" className="text-sm text-destructive">{t('errors.load')}</p>}

      {report.data && (
        <div className="space-y-5">
          {report.data.hinweis && (
            <div role="alert" className="rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100">
              <p>{report.data.hinweis}</p>
            </div>
          )}

          <section className="overflow-hidden rounded-xl border border-border bg-card">
            <table className="w-full text-sm">
              <tbody>
                <Row label={t('ugRuecklage.netIncome')} value={moneyFormat.format(report.data.jahresueberschuss)} />
                <Row label={t('ugRuecklage.lossCarryforward')} value={moneyFormat.format(report.data.verlustvortragVorjahr)} />
                <Row label={t('ugRuecklage.base')} value={moneyFormat.format(report.data.massgeblicherBetrag)} />
              </tbody>
              <tfoot className="border-t-2 border-border bg-muted/40 font-semibold">
                <tr>
                  <td className="px-4 py-3">{t('ugRuecklage.reserve')}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(report.data.ruecklage)}</td>
                </tr>
              </tfoot>
            </table>
          </section>

          <p className="text-xs text-muted-foreground">{t('ugRuecklage.note')}</p>

          {isOwner && (
            <div className="flex items-center gap-3">
              <Button disabled={!canBook || book.isPending} onClick={() => book.mutate()}>
                {book.isPending ? t('ugRuecklage.booking') : t('ugRuecklage.book')}
              </Button>
              {bookMessage && <span className="text-sm text-muted-foreground" role="status">{bookMessage}</span>}
            </div>
          )}
        </div>
      )}
    </main>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <tr className="border-t border-border first:border-t-0">
      <td className="px-4 py-3">{label}</td>
      <td className="px-4 py-3 text-right tabular-nums">{value}</td>
    </tr>
  )
}
