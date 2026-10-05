import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Input } from '@/components/ui/input'
import { useGuvReport, type GuvPosition } from './ustvaApi'

const moneyFormat = new Intl.NumberFormat('de-DE', {
  style: 'currency',
  currency: 'EUR',
})

export default function GuvReportPage() {
  const { t } = useTranslation('reports')
  const currentYear = new Date().getFullYear()
  const [jahr, setJahr] = useState(currentYear)
  const report = useGuvReport(jahr)
  const isLoss = (report.data?.jahresueberschuss ?? 0) < 0

  return (
    <main className="app-main" style={{ maxWidth: '1040px' }}>
      <div className="mb-6">
        <h1 className="text-2xl font-semibold">{t('guv.title')}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{t('guv.subtitle')}</p>
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

          <ReportSection
            title={t('guv.income')}
            positions={report.data.ertraege}
            totalLabel={t('guv.totalIncome')}
            total={report.data.ertraege.reduce((sum, p) => sum + p.betrag, 0)}
          />
          <ReportSection
            title={t('guv.expenses')}
            positions={report.data.aufwendungen}
            totalLabel={t('guv.totalExpenses')}
            total={report.data.aufwendungen.reduce((sum, p) => sum + p.betrag, 0)}
          />

          <section className="flex items-center justify-between rounded-xl border-2 border-primary/30 bg-primary/5 px-5 py-5">
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                {t('guv.result')}
              </p>
              <h2 className="mt-1 text-lg font-semibold">
                {isLoss ? t('guv.loss') : t('guv.profit')}
              </h2>
            </div>
            <span className="text-2xl font-semibold tabular-nums">
              {moneyFormat.format(report.data.jahresueberschuss)}
            </span>
          </section>
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
  positions: GuvPosition[]
  totalLabel: string
  total: number
}) {
  const { t } = useTranslation('reports')
  return (
    <section className="overflow-hidden rounded-xl border border-border bg-card">
      <h2 className="border-b border-border px-4 py-3 text-base font-semibold">{title}</h2>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[480px] text-sm">
          <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
            <tr>
              <th className="px-4 py-3">{t('guv.columns.description')}</th>
              <th className="px-4 py-3 text-right">{t('guv.columns.amount')}</th>
            </tr>
          </thead>
          <tbody>
            {positions.map((position) => (
              <tr key={position.bezeichnung} className="border-t border-border first:border-t-0">
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
