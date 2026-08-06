import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ApiError } from '@/lib/api'
import {
  listOpenItems,
  OpenItemStatus,
  type OpenItemListItem,
} from '@/lib/api/openItems'
import { Badge } from '@/components/ui/badge'
import { Button, buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
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
  MatchTier,
  useBankTransaction,
  useConfirmBankTransaction,
  useIgnoreBankTransaction,
  useMatchSuggestions,
  useUnmatchBankTransaction,
  type MatchCandidate,
} from './bankingApi'

interface AllocationDraft {
  openItemId: string
  amount: string
}

// BankTransaction.Amount is decimal(19,4); normalize to integer ten-thousandths
// so the client enforces the same exact sum without binary floating-point drift.
const MONEY_SCALE = 10_000

function errorDetail(error: unknown): string | null {
  if (!(error instanceof ApiError)) return null
  const body = error.body as
    | { detail?: string; title?: string; error?: string; errors?: Record<string, string[]> }
    | undefined
  return body?.detail ?? body?.error ?? Object.values(body?.errors ?? {})[0]?.[0] ?? body?.title ?? null
}

function toMoneyUnits(value: number | string): number | null {
  const parsed = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(parsed)) return null
  return Math.round(parsed * MONEY_SCALE)
}

async function loadOpenItems(status: OpenItemStatus): Promise<OpenItemListItem[]> {
  const items: OpenItemListItem[] = []
  let page = 1
  do {
    const response = await listOpenItems({ status, page, pageSize: 100 })
    items.push(...response.items)
    if (page * response.pageSize >= response.total) return items
    page += 1
  } while (true)
}

function reasonKey(reason: string): string | null {
  const keys: Record<string, string> = {
    'Rechnungsnummer im Verwendungszweck oder End-to-End-Verweis': 'reference',
    'Betrag exakt': 'exactAmount',
    'Teilbetrag möglich': 'partialAmount',
    'IBAN stimmt überein': 'iban',
    'Name der Gegenpartei stimmt weitgehend überein': 'name',
  }
  return keys[reason] ?? null
}

export default function ReconciliationMatchPage() {
  const { t, i18n } = useTranslation('banking')
  const { id = '' } = useParams<{ id: string }>()
  const locale = i18n.resolvedLanguage === 'en' ? 'en-GB' : 'de-DE'
  const transaction = useBankTransaction(id)
  const confirm = useConfirmBankTransaction(id)
  const unmatch = useUnmatchBankTransaction(id)
  const ignore = useIgnoreBankTransaction()
  const confirmed =
    transaction.data?.matchStatus === MatchStatus.Confirmed ||
    confirm.data?.matchStatus === MatchStatus.Confirmed
  const incoming = (transaction.data?.amount ?? 0) > 0
  const suggestions = useMatchSuggestions(id, incoming && !confirmed)
  const openItems = useQuery({
    queryKey: ['open-items', 'banking-allocation'],
    queryFn: async () => {
      const [open, partial] = await Promise.all([
        loadOpenItems(OpenItemStatus.Open),
        loadOpenItems(OpenItemStatus.PartiallyPaid),
      ])
      return [...open, ...partial]
    },
    enabled: incoming && !confirmed,
  })

  const [allocations, setAllocations] = useState<AllocationDraft[]>([])
  const [manualOpenItemId, setManualOpenItemId] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const initialized = useRef(false)

  useEffect(() => {
    initialized.current = false
    setAllocations([])
    setManualOpenItemId('')
    setMessage(null)
    setError(null)
  }, [id])

  useEffect(() => {
    if (initialized.current || !transaction.data || !suggestions.data) return
    initialized.current = true
    const high = suggestions.data.find((candidate) => candidate.tier === MatchTier.High)
    if (high && transaction.data.amount > 0) {
      setAllocations([
        { openItemId: high.openItemId, amount: high.suggestedAllocation.toFixed(4) },
      ])
    }
  }, [suggestions.data, transaction.data])

  const candidatesById = useMemo(
    () => new Map(suggestions.data?.map((candidate) => [candidate.openItemId, candidate]) ?? []),
    [suggestions.data],
  )
  const openItemsById = useMemo(
    () => new Map(openItems.data?.map((item) => [item.id, item]) ?? []),
    [openItems.data],
  )
  const selectableItems = useMemo(() => {
    const merged = new Map<string, { id: string; documentNumber: string; openAmount: number }>()
    suggestions.data?.forEach((candidate) => {
      merged.set(candidate.openItemId, {
        id: candidate.openItemId,
        documentNumber: candidate.documentNumber,
        openAmount: candidate.openAmount,
      })
    })
    openItems.data?.forEach((item) => {
      merged.set(item.id, {
        id: item.id,
        documentNumber: item.documentNumber,
        openAmount: item.openAmount,
      })
    })
    return [...merged.values()].sort((left, right) =>
      left.documentNumber.localeCompare(right.documentNumber),
    )
  }, [openItems.data, suggestions.data])

  const transactionUnits = toMoneyUnits(transaction.data?.amount ?? 0) ?? 0
  const allocationUnits = allocations.map((allocation) => toMoneyUnits(allocation.amount))
  const sumUnits = allocationUnits.reduce<number>(
    (sum, units) => sum + (units ?? 0),
    0,
  )
  const allocationsValid = allocations.length > 0 && allocations.every((allocation, index) => {
    const units = allocationUnits[index]
    const item = openItemsById.get(allocation.openItemId) ?? candidatesById.get(allocation.openItemId)
    const openUnits = item ? toMoneyUnits(item.openAmount) : null
    return units != null && units > 0 && openUnits != null && units <= openUnits
  })
  const exactSum = allocationsValid && sumUnits === transactionUnits
  const canConfirm = incoming && !confirmed && exactSum && !confirm.isPending

  function formatMoney(value: number): string {
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency: 'EUR',
      maximumFractionDigits: 4,
    }).format(value)
  }

  function formatDate(value: string): string {
    const parsed = new Date(`${value}T00:00:00`)
    return Number.isNaN(parsed.getTime())
      ? value
      : new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(parsed)
  }

  function acceptSuggestion(candidate: MatchCandidate) {
    setAllocations([
      {
        openItemId: candidate.openItemId,
        amount: candidate.suggestedAllocation.toFixed(4),
      },
    ])
    setMessage(t('match.suggestionAccepted', { document: candidate.documentNumber }))
    setError(null)
  }

  function addManualAllocation() {
    if (!manualOpenItemId || allocations.some((item) => item.openItemId === manualOpenItemId)) return
    const selected = selectableItems.find((item) => item.id === manualOpenItemId)
    if (!selected) return
    const remainingUnits = Math.max(0, transactionUnits - sumUnits)
    const openUnits = toMoneyUnits(selected.openAmount) ?? 0
    const amountUnits = Math.min(openUnits, remainingUnits || transactionUnits)
    setAllocations((current) => [
      ...current,
      { openItemId: selected.id, amount: (amountUnits / MONEY_SCALE).toFixed(4) },
    ])
    setManualOpenItemId('')
    setMessage(null)
    setError(null)
  }

  async function confirmMatch() {
    if (!canConfirm) return
    setMessage(null)
    setError(null)
    try {
      const result = await confirm.mutateAsync({
        allocations: allocations.map((allocation) => ({
          openItemId: allocation.openItemId,
          amount: Number(allocation.amount),
        })),
      })
      setMessage(result.alreadyConfirmed ? t('match.alreadyConfirmed') : t('match.confirmed'))
    } catch (confirmError) {
      setError(errorDetail(confirmError) ?? t('match.confirmError'))
    }
  }

  async function unmatchTransaction() {
    setMessage(null)
    setError(null)
    try {
      await unmatch.mutateAsync(id)
      confirm.reset()
      initialized.current = false
      setAllocations([])
      setMessage(t('match.unmatched'))
    } catch (unmatchError) {
      setError(errorDetail(unmatchError) ?? t('match.unmatchError'))
    }
  }

  async function ignoreTransaction() {
    setMessage(null)
    setError(null)
    try {
      await ignore.mutateAsync(id)
      setMessage(t('queue.ignored'))
    } catch (ignoreError) {
      setError(errorDetail(ignoreError) ?? t('queue.ignoreError'))
    }
  }

  if (transaction.isLoading) {
    return <main className="app-main"><p className="text-sm text-muted-foreground">{t('match.loading')}</p></main>
  }
  if (transaction.isError || !transaction.data) {
    return <main className="app-main"><p role="alert" className="text-sm text-destructive">{t('match.loadError')}</p></main>
  }

  const item = transaction.data
  const paymentId = confirm.data?.paymentId ?? item.matchedPaymentId

  return (
    <main className="app-main" style={{ maxWidth: '1280px' }}>
      <div className="mb-5 flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="mb-2 flex flex-wrap items-center gap-2">
            <Badge variant="outline">{t(`matchStatus.${item.matchStatus}`)}</Badge>
            <Badge variant={incoming ? 'secondary' : 'outline'}>
              {incoming ? t('match.incoming') : t('queue.noReceivable')}
            </Badge>
          </div>
          <h1 className="text-2xl font-semibold">
            {item.counterpartyName ?? t('queue.unknownCounterparty')}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {t('match.subtitle', {
              amount: formatMoney(item.amount),
              date: formatDate(item.valueDate),
            })}
          </p>
        </div>
        <Link to="/banking/queue" className={cn(buttonVariants({ variant: 'outline' }))}>
          {t('actions.backToQueue')}
        </Link>
      </div>

      <Card className="mb-5">
        <CardContent className="grid gap-4 pt-4 text-sm sm:grid-cols-3">
          <div>
            <p className="text-xs text-muted-foreground">{t('match.counterparty')}</p>
            <p className="mt-1 font-medium">{item.counterpartyName ?? '—'}</p>
            {item.counterpartyIban && <p className="mt-0.5 font-mono text-xs">{item.counterpartyIban}</p>}
          </div>
          <div className="sm:col-span-2">
            <p className="text-xs text-muted-foreground">{t('match.purpose')}</p>
            <p className="mt-1 font-medium">{item.purpose ?? '—'}</p>
            {item.endToEndId && <p className="mt-1 text-xs text-muted-foreground">{item.endToEndId}</p>}
          </div>
        </CardContent>
      </Card>

      {error && <p role="alert" className="mb-4 text-sm text-destructive">{error}</p>}
      {message && <p role="status" className="mb-4 text-sm font-medium text-primary">{message}</p>}

      {!incoming && (
        <Card className="border-amber-300/80 bg-amber-50/50 dark:border-amber-800 dark:bg-amber-950/20">
          <CardHeader>
            <CardTitle>{t('match.outgoingTitle')}</CardTitle>
            <p className="text-sm text-muted-foreground">{t('match.outgoingHint')}</p>
          </CardHeader>
          <CardContent>
            <Button
              variant="outline"
              disabled={ignore.isPending || item.matchStatus === MatchStatus.Ignored}
              onClick={() => void ignoreTransaction()}
            >
              {ignore.isPending ? t('queue.ignoring') : t('queue.ignore')}
            </Button>
          </CardContent>
        </Card>
      )}

      {confirmed && (
        <Card className="border-primary/30 bg-primary/5">
          <CardHeader>
            <CardTitle>{t('match.bookingTitle')}</CardTitle>
            <p className="text-sm text-muted-foreground">{t('match.bookingHint')}</p>
          </CardHeader>
          <CardContent>
            <p className="text-sm font-semibold text-primary">{t('match.bankReceivable')}</p>
            {paymentId && (
              <code className="mt-3 block overflow-x-auto rounded-md bg-card px-3 py-2 text-xs">
                {paymentId}
              </code>
            )}
            <Button
              className="mt-4"
              variant="destructive"
              disabled={unmatch.isPending}
              onClick={() => void unmatchTransaction()}
            >
              {unmatch.isPending ? t('match.unmatching') : t('match.unmatch')}
            </Button>
          </CardContent>
        </Card>
      )}

      {incoming && !confirmed && (
        <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,0.9fr)_minmax(460px,1.1fr)]">
          <Card className="overflow-hidden">
            <CardHeader className="border-b border-border bg-muted/35">
              <CardTitle>{t('match.suggestionsTitle')}</CardTitle>
              <p className="text-sm text-muted-foreground">{t('match.suggestionsHint')}</p>
            </CardHeader>
            <CardContent className="flex flex-col gap-3 pt-4">
              {suggestions.isLoading && <p className="text-sm text-muted-foreground">{t('match.suggestionsLoading')}</p>}
              {suggestions.isError && <p role="alert" className="text-sm text-destructive">{t('match.suggestionsError')}</p>}
              {suggestions.data?.map((candidate, index) => (
                <div
                  key={candidate.openItemId}
                  className={cn(
                    'rounded-lg border p-3',
                    candidate.tier === MatchTier.High
                      ? 'border-primary/30 bg-primary/5'
                      : 'border-border',
                  )}
                >
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <p className="font-semibold">{index + 1}. {candidate.documentNumber}</p>
                      <p className="mt-1 text-sm text-muted-foreground">
                        {t('match.openAndSuggested', {
                          open: formatMoney(candidate.openAmount),
                          suggested: formatMoney(candidate.suggestedAllocation),
                        })}
                      </p>
                    </div>
                    <Badge variant={candidate.tier === MatchTier.High ? 'secondary' : 'outline'}>
                      {t(`tier.${candidate.tier}`, { value: Math.round(candidate.score * 100) })}
                    </Badge>
                  </div>
                  {candidate.reasons.length > 0 && (
                    <ul className="mt-3 list-disc space-y-1 pl-5 text-xs text-muted-foreground">
                      {candidate.reasons.map((reason) => {
                        const key = reasonKey(reason)
                        return <li key={reason}>{key ? t(`reasons.${key}`) : reason}</li>
                      })}
                    </ul>
                  )}
                  <Button
                    className="mt-3"
                    size="sm"
                    variant="outline"
                    onClick={() => acceptSuggestion(candidate)}
                  >
                    {t('match.acceptSuggestion')}
                  </Button>
                </div>
              ))}
              {suggestions.data?.length === 0 && (
                <p className="text-sm text-muted-foreground">{t('match.noSuggestions')}</p>
              )}
            </CardContent>
          </Card>

          <div className="flex min-w-0 flex-col gap-5">
            <Card>
              <CardHeader>
                <CardTitle>{t('match.allocationsTitle')}</CardTitle>
                <p className="text-sm text-muted-foreground">{t('match.allocationsHint')}</p>
              </CardHeader>
              <CardContent>
                <div className="flex flex-col gap-2 sm:flex-row">
                  <Select
                    value={manualOpenItemId}
                    disabled={openItems.isLoading}
                    onChange={(event) => setManualOpenItemId(event.target.value)}
                  >
                    <option value="">{openItems.isLoading ? t('match.openItemsLoading') : t('match.chooseOpenItem')}</option>
                    {selectableItems.map((openItem) => (
                      <option
                        key={openItem.id}
                        value={openItem.id}
                        disabled={allocations.some((allocation) => allocation.openItemId === openItem.id)}
                      >
                        {openItem.documentNumber} · {formatMoney(openItem.openAmount)}
                      </option>
                    ))}
                  </Select>
                  <Button variant="outline" disabled={!manualOpenItemId} onClick={addManualAllocation}>
                    {t('match.addAllocation')}
                  </Button>
                </div>
                {openItems.isError && <p role="alert" className="mt-2 text-sm text-destructive">{t('match.openItemsError')}</p>}

                <div className="mt-4 overflow-x-auto rounded-lg border border-border">
                  <Table>
                    <TableHeader className="bg-muted/50">
                      <TableRow>
                        <TableHead>{t('match.invoice')}</TableHead>
                        <TableHead>{t('match.openAmount')}</TableHead>
                        <TableHead>{t('match.allocation')}</TableHead>
                        <TableHead><span className="sr-only">{t('match.remove')}</span></TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {allocations.map((allocation, index) => {
                        const linked = openItemsById.get(allocation.openItemId) ?? candidatesById.get(allocation.openItemId)
                        return (
                          <TableRow key={allocation.openItemId}>
                            <TableCell className="font-medium">
                              {linked?.documentNumber ?? allocation.openItemId}
                            </TableCell>
                            <TableCell className="whitespace-nowrap tabular-nums">
                              {linked ? formatMoney(linked.openAmount) : '—'}
                            </TableCell>
                            <TableCell className="min-w-36">
                              <Input
                                type="number"
                                min="0.01"
                                step="0.0001"
                                aria-label={t('match.allocationFor', { document: linked?.documentNumber ?? allocation.openItemId })}
                                value={allocation.amount}
                                onChange={(event) => {
                                  const amount = event.target.value
                                  setAllocations((current) => current.map((entry, currentIndex) =>
                                    currentIndex === index ? { ...entry, amount } : entry,
                                  ))
                                  setMessage(null)
                                  setError(null)
                                }}
                              />
                            </TableCell>
                            <TableCell className="text-right">
                              <Button
                                size="sm"
                                variant="ghost"
                                onClick={() => setAllocations((current) => current.filter((_, currentIndex) => currentIndex !== index))}
                              >
                                {t('match.remove')}
                              </Button>
                            </TableCell>
                          </TableRow>
                        )
                      })}
                      {allocations.length === 0 && (
                        <TableRow>
                          <TableCell colSpan={4} className="h-20 text-center text-muted-foreground">
                            {t('match.noAllocations')}
                          </TableCell>
                        </TableRow>
                      )}
                    </TableBody>
                  </Table>
                </div>

                <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4 text-sm">
                  <div>
                    <p>{t('match.allocationSum')}: <strong>{formatMoney(sumUnits / MONEY_SCALE)}</strong></p>
                    <p className="mt-0.5 text-muted-foreground">
                      {t('match.transactionAmount')}: {formatMoney(item.amount)}
                    </p>
                  </div>
                  {allocations.length > 1 && <Badge variant="secondary">{t('match.split', { count: allocations.length })}</Badge>}
                </div>
                {!exactSum && allocations.length > 0 && (
                  <p role="alert" className="mt-3 text-sm text-amber-700 dark:text-amber-300">
                    {allocationsValid
                      ? t('match.sumMismatch', { amount: formatMoney(Math.abs(transactionUnits - sumUnits) / MONEY_SCALE) })
                      : t('match.invalidAllocation')}
                  </p>
                )}
              </CardContent>
            </Card>

            <Card className="border-primary/25">
              <CardContent className="pt-4">
                <p className="text-sm font-semibold">{t('match.confirmTitle')}</p>
                <p className="mt-1 text-sm text-muted-foreground">{t('match.neverAutomatic')}</p>
                <Button
                  className="mt-4 w-full"
                  size="lg"
                  disabled={!canConfirm}
                  onClick={() => void confirmMatch()}
                >
                  {confirm.isPending ? t('match.confirming') : t('match.confirmAction')}
                </Button>
              </CardContent>
            </Card>
          </div>
        </div>
      )}
    </main>
  )
}
