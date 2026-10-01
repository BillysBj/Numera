import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Badge } from '@/components/ui/badge'
import { Button, buttonVariants } from '@/components/ui/button'
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
  MatchStatus,
  useBankTransactions,
  useIgnoreBankTransaction,
  type BankTransactionListItem,
} from './bankingApi'

const FILTER_STATUSES = [
  MatchStatus.Unmatched,
  MatchStatus.Suggested,
  MatchStatus.Review,
  MatchStatus.Confirmed,
  MatchStatus.Ignored,
] as const

function formatDate(value: string, locale: string): string {
  const parsed = new Date(`${value}T00:00:00`)
  return Number.isNaN(parsed.getTime())
    ? value
    : new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(parsed)
}

function formatMoney(value: number, locale: string): string {
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency: 'EUR',
    maximumFractionDigits: 4,
  }).format(value)
}

function ConfidenceBadge({ transaction }: { transaction: BankTransactionListItem }) {
  const { t } = useTranslation('banking')
  if (transaction.amount <= 0) {
    return <Badge variant="outline">{t('queue.noReceivable')}</Badge>
  }

  const high =
    transaction.matchStatus === MatchStatus.Suggested ||
    (transaction.confidenceScore != null && transaction.confidenceScore >= 0.85)
  const score = transaction.confidenceScore
  return (
    <Badge
      variant={high ? 'secondary' : 'outline'}
      className={cn(
        !high &&
          'border-amber-400 bg-amber-50 text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100',
      )}
    >
      {score == null
        ? t(high ? 'confidence.high' : 'confidence.review')
        : t(high ? 'confidence.highScore' : 'confidence.reviewScore', {
            value: Math.round(score * 100),
          })}
    </Badge>
  )
}

export default function ReconciliationQueuePage() {
  const { t, i18n } = useTranslation('banking')
  const locale = i18n.resolvedLanguage === 'en' ? 'en-GB' : 'de-DE'
  const [status, setStatus] = useState<MatchStatus | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const transactions = useBankTransactions(status)
  const ignore = useIgnoreBankTransaction()

  async function ignoreTransaction(id: string) {
    setMessage(null)
    setError(null)
    try {
      await ignore.mutateAsync(id)
      setMessage(t('queue.ignored'))
    } catch {
      setError(t('queue.ignoreError'))
    }
  }

  return (
    <main className="app-main app-main--wide">
      <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">{t('queue.title')}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t('queue.subtitle')}</p>
        </div>
        <Link to="/banking" className={cn(buttonVariants({ variant: 'outline' }))}>
          {t('actions.backToAccounts')}
        </Link>
      </div>

      <section className="mb-5 rounded-xl border border-border bg-card p-4">
        <label className="flex max-w-xs flex-col gap-1.5 text-sm font-medium">
          {t('queue.filterLabel')}
          <Select
            value={status ?? ''}
            onChange={(event) =>
              setStatus(
                event.target.value === ''
                  ? null
                  : (Number(event.target.value) as MatchStatus),
              )
            }
          >
            <option value="">{t('queue.openStatuses')}</option>
            {FILTER_STATUSES.map((value) => (
              <option key={value} value={value}>{t(`matchStatus.${value}`)}</option>
            ))}
          </Select>
        </label>
      </section>

      {transactions.isError && (
        <p role="alert" className="mb-4 text-sm text-destructive">{t('queue.loadError')}</p>
      )}
      {error && <p role="alert" className="mb-4 text-sm text-destructive">{error}</p>}
      {message && <p role="status" className="mb-4 text-sm font-medium text-primary">{message}</p>}

      <section className="overflow-x-auto rounded-xl border border-border bg-card">
        <Table>
          <TableHeader className="bg-muted/60">
            <TableRow>
              <TableHead>{t('queue.columns.transaction')}</TableHead>
              <TableHead>{t('queue.columns.date')}</TableHead>
              <TableHead>{t('queue.columns.status')}</TableHead>
              <TableHead>{t('queue.columns.confidence')}</TableHead>
              <TableHead className="text-right">{t('queue.columns.amount')}</TableHead>
              <TableHead className="text-right">{t('queue.columns.actions')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {transactions.isLoading && (
              <TableRow>
                <TableCell colSpan={6} className="h-28 text-center text-muted-foreground">
                  {t('queue.loading')}
                </TableCell>
              </TableRow>
            )}
            {transactions.data?.items.map((transaction) => {
              const outgoing = transaction.amount <= 0
              const ignoring = ignore.isPending && ignore.variables === transaction.id
              return (
                <TableRow key={transaction.id} className={outgoing ? 'bg-muted/25' : undefined}>
                  <TableCell className="min-w-64">
                    <p className="font-semibold">
                      {transaction.counterpartyName ?? t('queue.unknownCounterparty')}
                    </p>
                    {transaction.counterpartyIban && (
                      <p className="mt-0.5 font-mono text-xs text-muted-foreground">
                        {transaction.counterpartyIban}
                      </p>
                    )}
                    <p className="mt-1 max-w-xl text-sm text-muted-foreground">
                      {transaction.purpose ?? t('queue.noPurpose')}
                    </p>
                  </TableCell>
                  <TableCell className="whitespace-nowrap">
                    {formatDate(transaction.valueDate, locale)}
                  </TableCell>
                  <TableCell>
                    <Badge variant="outline">{t(`matchStatus.${transaction.matchStatus}`)}</Badge>
                  </TableCell>
                  <TableCell><ConfidenceBadge transaction={transaction} /></TableCell>
                  <TableCell
                    className={cn(
                      'whitespace-nowrap text-right font-semibold tabular-nums',
                      outgoing && 'text-muted-foreground',
                    )}
                  >
                    {formatMoney(transaction.amount, locale)}
                  </TableCell>
                  <TableCell className="text-right">
                    {outgoing ? (
                      <Button
                        size="sm"
                        variant="outline"
                        disabled={ignore.isPending || transaction.matchStatus === MatchStatus.Ignored}
                        onClick={() => void ignoreTransaction(transaction.id)}
                      >
                        {ignoring ? t('queue.ignoring') : t('queue.ignore')}
                      </Button>
                    ) : (
                      <Link
                        to={`/banking/tx/${transaction.id}`}
                        className={cn(buttonVariants({ size: 'sm', variant: 'outline' }))}
                      >
                        {transaction.matchStatus === MatchStatus.Confirmed
                          ? t('queue.view')
                          : t('queue.review')}
                      </Link>
                    )}
                  </TableCell>
                </TableRow>
              )
            })}
            {transactions.data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={6} className="h-28 text-center text-muted-foreground">
                  {t('queue.empty')}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
        {transactions.data && transactions.data.total > transactions.data.items.length && (
          <p className="border-t border-border px-4 py-3 text-xs text-muted-foreground">
            {t('queue.showing', {
              shown: transactions.data.items.length,
              total: transactions.data.total,
            })}
          </p>
        )}
      </section>
    </main>
  )
}
