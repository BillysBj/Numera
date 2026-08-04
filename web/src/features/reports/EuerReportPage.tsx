import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { downloadEuerPdf, useEuerReport, type EuerLine } from './ustvaApi'

const moneyFormat = new Intl.NumberFormat('de-DE', {
  style: 'currency',
  currency: 'EUR',
})

function isoToday(): string {
  const now = new Date()
  const offset = now.getTimezoneOffset()
  return new Date(now.getTime() - offset * 60_000).toISOString().slice(0, 10)
}

export default function EuerReportPage() {
  const { t } = useTranslation('reports')
  const currentYear = new Date().getFullYear()
  const [jahr, setJahr] = useState(currentYear)
  const [from, setFrom] = useState(`${currentYear}-01-01`)
  const [to, setTo] = useState(isoToday())
  const [downloading, setDownloading] = useState(false)
  const [downloadError, setDownloadError] = useState(false)
  const invalidRange = from.length > 0 && to.length > 0 && from > to
  const report = useEuerReport(jahr, from, to)

  function updateYear(value: number) {
    setJahr(value)
    setFrom(`${value}-01-01`)
    setTo(value === currentYear ? isoToday() : `${value}-12-31`)
  }

  async function runDownload() {
    setDownloading(true)
    setDownloadError(false)
    try {
      await downloadEuerPdf(jahr, from, to)
    } catch {
      setDownloadError(true)
    } finally {
      setDownloading(false)
    }
  }

  return (
    <main className="app-main" style={{ maxWidth: '1040px' }}>
      <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold">{t('euer.title')}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t('euer.subtitle')}</p>
        </div>
        <Button
          disabled={downloading || !report.data || invalidRange}
          onClick={() => void runDownload()}
        >
          {downloading ? t('actions.downloading') : t('actions.euerPdf')}
        </Button>
      </div>

      <section className="mb-5 rounded-xl border border-border bg-card p-4">
        <div className="flex flex-wrap items-end gap-3">
          <label className="flex flex-col gap-1.5 text-sm font-medium">
            {t('filters.year')}
            <Input
              className="w-28"
              type="number"
              min={2000}
              max={2100}
              value={jahr}
              onChange={(event) => updateYear(Number(event.target.value))}
            />
          </label>
          <label className="flex flex-col gap-1.5 text-sm font-medium">
            {t('filters.from')}
            <Input type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
          </label>
          <label className="flex flex-col gap-1.5 text-sm font-medium">
            {t('filters.to')}
            <Input type="date" value={to} onChange={(event) => setTo(event.target.value)} />
          </label>
        </div>
        {invalidRange && (
          <p role="alert" className="mt-3 text-sm text-destructive">{t('errors.invalidRange')}</p>
        )}
      </section>

      {downloadError && (
        <p role="alert" className="mb-4 text-sm text-destructive">{t('errors.download')}</p>
      )}
      {report.isLoading && <p className="text-sm text-muted-foreground">{t('loading')}</p>}
      {report.isError && (
        <p role="alert" className="text-sm text-destructive">{t('errors.load')}</p>
      )}

      {report.data && (
        <div className="space-y-5">
          {report.data.isExpenseDataIncomplete && (
            <div role="alert" className="rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100">
              <p className="font-semibold">{t('euer.incompleteTitle')}</p>
              <p className="mt-1">{report.data.hinweis ?? t('euer.incompleteFallback')}</p>
            </div>
          )}

          <ReportSection
            title={t('euer.income')}
            lines={report.data.betriebseinnahmen}
            totalLabel={t('euer.totalIncome')}
            total={report.data.summeEinnahmen}
          />
          <ReportSection
            title={t('euer.expenses')}
            lines={report.data.betriebsausgaben}
            totalLabel={t('euer.totalExpenses')}
            total={report.data.summeAusgaben}
          />

          <section className="flex items-center justify-between rounded-xl border-2 border-primary/30 bg-primary/5 px-5 py-5">
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                {t('euer.result')}
              </p>
              <h2 className="mt-1 text-lg font-semibold">{t('euer.profit')}</h2>
            </div>
            <span className="text-2xl font-semibold tabular-nums">
              {moneyFormat.format(report.data.gewinn)}
            </span>
          </section>
        </div>
      )}
    </main>
  )
}

function ReportSection({
  title,
  lines,
  totalLabel,
  total,
}: {
  title: string
  lines: EuerLine[]
  totalLabel: string
  total: number
}) {
  const { t } = useTranslation('reports')
  return (
    <section className="overflow-hidden rounded-xl border border-border bg-card">
      <h2 className="border-b border-border px-4 py-3 text-base font-semibold">{title}</h2>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[560px] text-sm">
          <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
            <tr>
              <th className="w-28 px-4 py-3">{t('euer.columns.line')}</th>
              <th className="px-4 py-3">{t('euer.columns.description')}</th>
              <th className="px-4 py-3 text-right">{t('euer.columns.amount')}</th>
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => (
              <tr key={line.zeile} className="border-t border-border first:border-t-0">
                <td className="px-4 py-3 tnum">{line.zeile}</td>
                <td className="px-4 py-3">{line.bezeichnung}</td>
                <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(line.betrag)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot className="border-t-2 border-border bg-muted/40 font-semibold">
            <tr>
              <td colSpan={2} className="px-4 py-3">{totalLabel}</td>
              <td className="px-4 py-3 text-right tabular-nums">{moneyFormat.format(total)}</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </section>
  )
}
