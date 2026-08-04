import { Link, useLocation } from 'react-router-dom'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Badge } from '@/components/ui/badge'
import { buttonVariants } from '@/components/ui/button'
import { Select } from '@/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { cn } from '@/lib/utils'
import {
  ReceiptSource,
  ReceiptStatus,
  useReceipts,
  type ReceiptListItem,
} from './belegeApi'
import MailboxAddressCard from './MailboxAddressCard'

const FILTER_STATUSES = [
  ReceiptStatus.Captured,
  ReceiptStatus.Extracted,
  ReceiptStatus.Reviewed,
  ReceiptStatus.Booked,
  ReceiptStatus.Duplicate,
  ReceiptStatus.Quarantined,
] as const

const money = new Intl.NumberFormat('de-DE', {
  style: 'currency',
  currency: 'EUR',
})
const date = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium' })

function formatDate(value: string | null): string {
  if (!value) return '—'
  const parsed = new Date(`${value}T00:00:00`)
  return Number.isNaN(parsed.getTime()) ? value : date.format(parsed)
}

function formatMoney(value: number | null, currency: string | null): string {
  if (value == null) return '—'
  if (!currency || currency === 'EUR') return money.format(value)
  return new Intl.NumberFormat('de-DE', { style: 'currency', currency }).format(value)
}

function StatusBadge({ status }: { status: ReceiptStatus }) {
  const { t } = useTranslation('belege')
  const exceptional = status === ReceiptStatus.Quarantined || status === ReceiptStatus.Rejected
  return (
    <Badge
      variant={exceptional ? 'destructive' : status === ReceiptStatus.Booked ? 'default' : 'secondary'}
      className={status === ReceiptStatus.Duplicate ? 'border border-amber-300 bg-amber-50 text-amber-800 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200' : undefined}
    >
      {t(`status.${status}`)}
    </Badge>
  )
}

function SourceBadge({ source }: { source: ReceiptSource }) {
  const { t } = useTranslation('belege')
  return (
    <Badge variant="outline" className="whitespace-nowrap">
      {t(`source.${source}`)}
    </Badge>
  )
}

function ReceiptRow({ receipt }: { receipt: ReceiptListItem }) {
  const { t } = useTranslation('belege')
  return (
    <TableRow
      className={cn(
        receipt.status === ReceiptStatus.Quarantined && 'bg-destructive/5',
        receipt.status === ReceiptStatus.Duplicate && 'bg-amber-50/60 dark:bg-amber-950/20',
      )}
    >
      <TableCell>
        <Link to={`/belege/${receipt.id}`} className="font-semibold text-primary hover:underline">
          {receipt.supplierName ?? receipt.originalFileName ?? t('queue.unknownSupplier')}
        </Link>
        {receipt.invoiceNumber && (
          <p className="mt-0.5 text-xs text-muted-foreground">{receipt.invoiceNumber}</p>
        )}
      </TableCell>
      <TableCell><SourceBadge source={receipt.source} /></TableCell>
      <TableCell><StatusBadge status={receipt.status} /></TableCell>
      <TableCell>{formatDate(receipt.invoiceDate)}</TableCell>
      <TableCell className="text-right font-medium tabular-nums">
        {formatMoney(receipt.grossAmount, receipt.currency)}
      </TableCell>
    </TableRow>
  )
}

export default function BelegReviewQueuePage() {
  const { t } = useTranslation('belege')
  const location = useLocation()
  const notice = (location.state as { captureNotice?: string } | null)?.captureNotice
  const [status, setStatus] = useState<ReceiptStatus | null>(null)
  const receipts = useReceipts(status)

  return (
    <main className="app-main">
      <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">{t('queue.title')}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t('queue.subtitle')}</p>
        </div>
        <Link to="/belege/capture" className={cn(buttonVariants())}>
          {t('actions.capture')}
        </Link>
      </div>

      {notice && (
        <div
          role="status"
          className={cn(
            'mb-5 rounded-lg border px-4 py-3 text-sm font-medium',
            notice === 'quarantined'
              ? 'border-destructive/30 bg-destructive/8 text-destructive'
              : notice === 'duplicate'
                ? 'border-amber-300 bg-amber-50 text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100'
                : 'border-primary/25 bg-primary/8 text-primary',
          )}
        >
          {t(`capture.notices.${notice}`)}
        </div>
      )}

      <div className="mb-5 grid gap-5 lg:grid-cols-[1.5fr_1fr]">
        <section className="rounded-xl border border-border bg-card p-4">
          <label className="flex max-w-xs flex-col gap-1.5 text-sm font-medium">
            {t('queue.filterLabel')}
            <Select
              value={status ?? ''}
              onChange={(event) =>
                setStatus(event.target.value === '' ? null : Number(event.target.value) as ReceiptStatus)
              }
            >
              <option value="">{t('queue.allStatuses')}</option>
              {FILTER_STATUSES.map((value) => (
                <option key={value} value={value}>{t(`status.${value}`)}</option>
              ))}
            </Select>
          </label>
        </section>
        <MailboxAddressCard />
      </div>

      {receipts.isError && (
        <p role="alert" className="mb-4 text-sm text-destructive">{t('queue.error')}</p>
      )}

      <section className="overflow-hidden rounded-xl border border-border bg-card">
        <Table>
          <TableHeader className="bg-muted/60">
            <TableRow>
              <TableHead>{t('queue.columns.supplier')}</TableHead>
              <TableHead>{t('queue.columns.source')}</TableHead>
              <TableHead>{t('queue.columns.status')}</TableHead>
              <TableHead>{t('queue.columns.date')}</TableHead>
              <TableHead className="text-right">{t('queue.columns.amount')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {receipts.isLoading && (
              <TableRow><TableCell colSpan={5} className="h-28 text-center text-muted-foreground">{t('queue.loading')}</TableCell></TableRow>
            )}
            {receipts.data?.items.map((receipt) => <ReceiptRow key={receipt.id} receipt={receipt} />)}
            {receipts.data?.items.length === 0 && (
              <TableRow><TableCell colSpan={5} className="h-28 text-center text-muted-foreground">{t('queue.empty')}</TableCell></TableRow>
            )}
          </TableBody>
        </Table>
        {receipts.data && receipts.data.total > receipts.data.items.length && (
          <p className="border-t border-border px-4 py-3 text-xs text-muted-foreground">
            {t('queue.showing', { shown: receipts.data.items.length, total: receipts.data.total })}
          </p>
        )}
      </section>
    </main>
  )
}
