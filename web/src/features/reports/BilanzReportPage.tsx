import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Input } from '@/components/ui/input'
import { useBilanzReport, type BilanzPosition } from './ustvaApi'

const moneyFormat = new Intl.NumberFormat('de-DE', {
  style: 'currency',
  currency: 'EUR',
})

export default function BilanzReportPage() {
  const { t } = useTranslation('reports')
  const currentYear = new Date().getFullYear()
  const [jahr, setJahr] = useState(currentYear)
  const report = useBilanzReport(jahr)
  const hasDifference = (report.data?.bilanzDifferenz ?? 0) !== 0

  return (
    <main className="app-main" style={{ maxWidth: '1040px' }}>
      <div className="mb-6">
        <h1 className="text-2xl font-semibold">{t('bilanz.title')}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{t('bilanz.subtitle')}</p>
      </div>

      <section className="mb-5 rounded-xl border border-border bg-card p-4">
        <label className="flex flex-col gap-1.5 text-sm font-medium">
          {t('filters.year')}
          <Input
            className="w-28"
            type="number"
            min={2000}
            max={2100}
            value={jahr}
            onChange={(event) => setJahr(Number(event.target.value))}
          />
        </label>
      </section>

      {report.isLoading && <p className="text-sm text-muted-foreground">{t('loading')}</p>}
      {report.isError && (
        <p role="alert" className="text-sm text-destructive">{t('errors.load')}</p>
      )}

      {report.data && (
        <div className="space-y-5">
          {report.data.hinweis && (
            <div role="alert" className="rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100">
              <p>{report.data.hinweis}</p>
            </div>
          )}

          {!report.data.hinweis && (
            <div className="grid gap-5 lg:grid-cols-2">
              <ReportSection
                title={t('bilanz.assets')}
                positions={report.data.aktiva}
                totalLabel={t('bilanz.totalAssets')}
                total={report.data.summeAktiva}
              />
              <ReportSection
                title={t('bilanz.liabilities')}
                positions={report.data.passiva}
                totalLabel={t('bilanz.totalLiabilities')}
                total={report.data.summePassiva}
              />
            </div>
          )}

          {!report.data.hinweis && (
            hasDifference ? (
              <div role="alert" className="rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100">
                <p className="font-semibold">
                  {t('bilanz.difference')}: {moneyFormat.format(report.data.bilanzDifferenz)}
                </p>
                <p className="mt-1">{t('bilanz.differenceHint')}</p>
              </div>
            ) : (
              <div className="rounded-xl border border-emerald-300 bg-emerald-50 px-4 py-3 text-sm text-emerald-900 dark:border-emerald-800 dark:bg-emerald-950 dark:text-emerald-100">
                <p className="font-semibold">{t('bilanz.balanced')}</p>
              </div>
            )
          )}
        </div>
      )}
    </main>
  )
}

function ReportSection({
  title,
  positions,
  totalLabel,
  total,
}: {
  title: string
  positions: BilanzPosition[]
  totalLabel: string
  total: number
}) {
  return (
    <section className="overflow-hidden rounded-xl border border-border bg-card">
      <h2 className="border-b border-border px-4 py-3 text-base font-semibold">{title}</h2>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[320px] text-sm">
          <tbody>
            {positions.map((position, index) => (
              <tr
                key={`${position.gruppe}-${position.bezeichnung}-${index}`}
                className="border-t border-border first:border-t-0"
              >
                <td className="px-4 py-3">{position.bezeichnung}</td>
                <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(position.betrag)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot className="border-t-2 border-border bg-muted/40 font-semibold">
            <tr>
              <td className="px-4 py-3">{totalLabel}</td>
              <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(total)}</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </section>
  )
}
