import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import {
  downloadUstVaPdf,
  downloadUstVaXml,
  useUstVaEntries,
  useUstVaReport,
  type UstVaDrillDownEntry,
  type UstVaLine,
} from './ustvaApi'

const moneyFormat = new Intl.NumberFormat('de-DE', {
  style: 'currency',
  currency: 'EUR',
})
const dateFormat = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium' })
const PERIODS = [
  '01', '02', '03', '04', '05', '06',
  '07', '08', '09', '10', '11', '12',
  '41', '42', '43', '44',
] as const

function formatDate(value: string | null): string {
  if (!value) return '—'
  const date = new Date(`${value}T00:00:00`)
  return Number.isNaN(date.getTime()) ? value : dateFormat.format(date)
}

function formatAmount(value: number | null): string {
  return value == null ? '—' : moneyFormat.format(value)
}

export default function UstVaPruefansichtPage() {
  const { t } = useTranslation('reports')
  const today = new Date()
  const [jahr, setJahr] = useState(today.getFullYear())
  const [zeitraum, setZeitraum] = useState(
    String(today.getMonth() + 1).padStart(2, '0'),
  )
  const [expandedKz, setExpandedKz] = useState<string | null>(null)
  const [downloading, setDownloading] = useState<'xml' | 'pdf' | null>(null)
  const [downloadError, setDownloadError] = useState(false)
  const report = useUstVaReport(jahr, zeitraum)

  async function runDownload(kind: 'xml' | 'pdf') {
    setDownloading(kind)
    setDownloadError(false)
    try {
      await (kind === 'xml'
        ? downloadUstVaXml(jahr, zeitraum)
        : downloadUstVaPdf(jahr, zeitraum))
    } catch {
      setDownloadError(true)
    } finally {
      setDownloading(null)
    }
  }

  return (
    <main className="app-main" style={{ maxWidth: '1120px' }}>
      <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold">{t('ustva.title')}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t('ustva.subtitle')}</p>
        </div>
        {!report.data?.isKleinunternehmer && (
          <div className="flex flex-wrap gap-2">
            <Button
              variant="outline"
              disabled={downloading !== null || !report.data}
              onClick={() => void runDownload('xml')}
            >
              {downloading === 'xml' ? t('actions.downloading') : t('actions.xml')}
            </Button>
            <Button
              disabled={downloading !== null || !report.data}
              onClick={() => void runDownload('pdf')}
            >
              {downloading === 'pdf' ? t('actions.downloading') : t('actions.ustvaPdf')}
            </Button>
          </div>
        )}
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
              onChange={(event) => {
                setJahr(Number(event.target.value))
                setExpandedKz(null)
              }}
            />
          </label>
          <label className="flex min-w-64 flex-col gap-1.5 text-sm font-medium">
            {t('filters.period')}
            <Select
              value={zeitraum}
              onChange={(event) => {
                setZeitraum(event.target.value)
                setExpandedKz(null)
              }}
            >
              {PERIODS.map((period) => (
                <option key={period} value={period}>
                  {period} · {t(`periods.${period}`)}
                </option>
              ))}
            </Select>
          </label>
        </div>
      </section>

      {downloadError && (
        <p role="alert" className="mb-4 text-sm text-destructive">
          {t('errors.download')}
        </p>
      )}
      {report.isLoading && <p className="text-sm text-muted-foreground">{t('loading')}</p>}
      {report.isError && (
        <p role="alert" className="text-sm text-destructive">{t('errors.load')}</p>
      )}

      {report.data?.isKleinunternehmer ? (
        <section className="rounded-xl border border-dashed border-border bg-card px-6 py-12 text-center">
          <p className="text-lg font-semibold">{t('ustva.kleinunternehmer.title')}</p>
          <p className="mx-auto mt-2 max-w-xl text-sm text-muted-foreground">
            {t('ustva.kleinunternehmer.description')}
          </p>
        </section>
      ) : report.data ? (
        <>
          <section className="mb-4 flex flex-wrap items-center gap-3 rounded-xl border border-border bg-card px-4 py-3">
            <span className="text-sm text-muted-foreground">{t('ustva.taxationType')}</span>
            <span className="text-sm font-semibold">
              {t(`ustva.taxationTypes.${report.data.besteuerungsart}`)}
            </span>
            {!report.data.isFestgeschrieben && (
              <Badge variant="secondary" className="border border-amber-300 bg-amber-50 text-amber-800 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200">
                {t('ustva.provisional')}
              </Badge>
            )}
          </section>

          <section className="overflow-hidden rounded-xl border border-border bg-card">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[720px] border-collapse text-sm">
                <thead className="bg-muted/60 text-left text-xs uppercase tracking-wide text-muted-foreground">
                  <tr>
                    <th className="w-24 px-4 py-3">{t('ustva.columns.kz')}</th>
                    <th className="px-4 py-3">{t('ustva.columns.description')}</th>
                    <th className="px-4 py-3 text-right">{t('ustva.columns.taxBase')}</th>
                    <th className="px-4 py-3 text-right">{t('ustva.columns.tax')}</th>
                  </tr>
                </thead>
                <tbody>
                  {report.data.lines.map((line) => (
                    <UstVaRow
                      key={line.kz}
                      line={line}
                      jahr={jahr}
                      zeitraum={zeitraum}
                      expanded={expandedKz === line.kz}
                      onToggle={() => setExpandedKz((current) => current === line.kz ? null : line.kz)}
                    />
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <p className="mt-4 rounded-lg border border-border bg-muted/40 px-4 py-3 text-sm text-muted-foreground">
            {report.data.hinweis ?? t('ustva.inputVatNote')}
          </p>
        </>
      ) : null}
    </main>
  )
}

function UstVaRow({
  line,
  jahr,
  zeitraum,
  expanded,
  onToggle,
}: {
  line: UstVaLine
  jahr: number
  zeitraum: string
  expanded: boolean
  onToggle: () => void
}) {
  const { t } = useTranslation('reports')
  const drillDown = useUstVaEntries(line.kz, jahr, zeitraum, expanded)

  return (
    <>
      <tr
        className={
          line.isComputed
            ? 'border-t-2 border-border bg-muted/40 font-semibold hover:bg-muted/60'
            : 'border-t border-border first:border-t-0 hover:bg-muted/30'
        }
      >
        <td className="px-4 py-3 align-top">
          <button
            type="button"
            onClick={onToggle}
            aria-expanded={expanded}
            className="inline-flex items-center gap-2 rounded text-left font-semibold text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            <span aria-hidden="true" className="w-3 text-xs">{expanded ? '▾' : '▸'}</span>
            <span className="tnum">{line.kz}</span>
          </button>
        </td>
        <td className="px-4 py-3 align-top">{line.bezeichnung}</td>
        <td className="px-4 py-3 text-right align-top tabular-nums">
          {formatAmount(line.bemessungsgrundlage)}
        </td>
        <td className="px-4 py-3 text-right align-top tabular-nums">
          {formatAmount(line.steuer)}
        </td>
      </tr>
      {expanded && (
        <tr className="border-t border-border bg-muted/20">
          <td colSpan={4} className="px-4 py-4 sm:px-8">
            {drillDown.isLoading && (
              <p className="text-sm text-muted-foreground">{t('loading')}</p>
            )}
            {drillDown.isError && (
              <p role="alert" className="text-sm text-destructive">{t('errors.drillDown')}</p>
            )}
            {drillDown.data && (
              <div>
                <p className="mb-3 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                  {t(`ustva.recognitionBasis.${drillDown.data.recognitionBasis}`)}
                </p>
                {drillDown.data.entries.length === 0 ? (
                  <p className="text-sm text-muted-foreground">{t('ustva.drillDown.empty')}</p>
                ) : drillDown.data.recognitionBasis === 'journalEntryDate' ? (
                  <JournalEntries entries={drillDown.data.entries} />
                ) : (
                  <PaymentEntries entries={drillDown.data.entries} />
                )}
              </div>
            )}
          </td>
        </tr>
      )}
    </>
  )
}

function JournalEntries({ entries }: { entries: UstVaDrillDownEntry[] }) {
  const { t } = useTranslation('reports')
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[680px] text-xs">
        <thead className="text-left text-muted-foreground">
          <tr>
            <th className="pb-2 pr-4">{t('ustva.drillDown.date')}</th>
            <th className="pb-2 pr-4">{t('ustva.drillDown.journal')}</th>
            <th className="pb-2 pr-4">{t('ustva.drillDown.source')}</th>
            <th className="pb-2 pr-4">{t('ustva.drillDown.description')}</th>
            <th className="pb-2 pr-4 text-right">{t('ustva.drillDown.net')}</th>
            <th className="pb-2 text-right">{t('ustva.drillDown.vat')}</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((entry) => (
            <tr key={entry.journalEntryId ?? `${entry.sourceRef}-${entry.entryDate}`} className="border-t border-border">
              <td className="py-2 pr-4">{formatDate(entry.entryDate)}</td>
              <td className="py-2 pr-4 tnum">{entry.journalNumber ?? '—'}</td>
              <td className="py-2 pr-4">{entry.sourceRef ?? '—'}</td>
              <td className="py-2 pr-4">{entry.description ?? '—'}</td>
              <td className="py-2 pr-4 text-right tabular-nums">{moneyFormat.format(entry.attributedNet)}</td>
              <td className="py-2 text-right tabular-nums">{moneyFormat.format(entry.attributedVat)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function PaymentEntries({ entries }: { entries: UstVaDrillDownEntry[] }) {
  const { t } = useTranslation('reports')
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[680px] text-xs">
        <thead className="text-left text-muted-foreground">
          <tr>
            <th className="pb-2 pr-4">{t('ustva.drillDown.paymentDate')}</th>
            <th className="pb-2 pr-4">{t('ustva.drillDown.document')}</th>
            <th className="pb-2 pr-4">{t('ustva.drillDown.invoiceDate')}</th>
            <th className="pb-2 pr-4">{t('ustva.drillDown.reference')}</th>
            <th className="pb-2 pr-4 text-right">{t('ustva.drillDown.net')}</th>
            <th className="pb-2 text-right">{t('ustva.drillDown.vat')}</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((entry) => (
            <tr key={entry.paymentId ?? `${entry.documentId}-${entry.paymentValueDate}`} className="border-t border-border">
              <td className="py-2 pr-4">{formatDate(entry.paymentValueDate)}</td>
              <td className="py-2 pr-4 font-medium">{entry.documentNumber ?? '—'}</td>
              <td className="py-2 pr-4">{formatDate(entry.invoiceDate)}</td>
              <td className="py-2 pr-4">{entry.paymentReference ?? '—'}</td>
              <td className="py-2 pr-4 text-right tabular-nums">{moneyFormat.format(entry.attributedNet)}</td>
              <td className="py-2 text-right tabular-nums">{moneyFormat.format(entry.attributedVat)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
