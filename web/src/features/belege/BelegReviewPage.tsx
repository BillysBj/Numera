import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { listPartners } from '@/lib/api/partners'
import { ApiError } from '@/lib/api'
import { Badge } from '@/components/ui/badge'
import { Button, buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
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
  PostingDirection,
  ReceiptSource,
  ReceiptStatus,
  useConfirmBookReceipt,
  useReceipt,
  useReceiptOriginal,
  useReceiptProposal,
  useReviewReceipt,
  type ReceiptDetail,
  type ReceiptFieldConfidence,
  type ReviewReceiptRequest,
} from './belegeApi'

const money = new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' })
const number = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 2 })
const date = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium' })

interface ReviewDraft {
  supplierPartnerId: string
  expenseAccountOverride: string
  vatRatePercent: string
  netAmount: string
  vatAmount: string
  grossAmount: string
  invoiceNumber: string
  invoiceDate: string
  expenseDate: string
}

function draftFromReceipt(receipt: ReceiptDetail): ReviewDraft {
  return {
    supplierPartnerId: receipt.matchedPartnerId ?? '',
    expenseAccountOverride: receipt.expenseAccountOverride ?? '',
    vatRatePercent: receipt.vatRatePercent == null ? '' : String(receipt.vatRatePercent),
    netAmount: receipt.netAmount == null ? '' : String(receipt.netAmount),
    vatAmount: receipt.vatAmount == null ? '' : String(receipt.vatAmount),
    grossAmount: receipt.grossAmount == null ? '' : String(receipt.grossAmount),
    invoiceNumber: receipt.invoiceNumber ?? '',
    invoiceDate: receipt.invoiceDate ?? '',
    expenseDate: receipt.expenseDate ?? receipt.invoiceDate ?? '',
  }
}

const EMPTY_DRAFT: ReviewDraft = {
  supplierPartnerId: '',
  expenseAccountOverride: '',
  vatRatePercent: '',
  netAmount: '',
  vatAmount: '',
  grossAmount: '',
  invoiceNumber: '',
  invoiceDate: '',
  expenseDate: '',
}

function formatDate(value: string | null): string {
  if (!value) return '—'
  const parsed = new Date(`${value}T00:00:00`)
  return Number.isNaN(parsed.getTime()) ? value : date.format(parsed)
}

function formatMoney(value: number, currency = 'EUR'): string {
  if (currency === 'EUR') return money.format(value)
  return new Intl.NumberFormat('de-DE', { style: 'currency', currency }).format(value)
}

function errorDetail(error: unknown): string | null {
  if (!(error instanceof ApiError)) return null
  const body = error.body as
    | { detail?: string; title?: string; errors?: Record<string, string[]> }
    | undefined
  return body?.detail ?? Object.values(body?.errors ?? {})[0]?.[0] ?? body?.title ?? null
}

function ConfidenceBadge({ value }: { value: number | null | undefined }) {
  const { t } = useTranslation('belege')
  if (value == null) {
    return (
      <Badge className="border border-amber-300 bg-amber-50 text-amber-800 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200">
        {t('confidence.manual')}
      </Badge>
    )
  }
  const level = value < 0.8 ? 'low' : value < 0.9 ? 'medium' : 'high'
  return (
    <Badge
      variant={level === 'high' ? 'secondary' : 'outline'}
      className={level === 'low' ? 'border-amber-400 bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-100' : undefined}
    >
      {t(`confidence.${level}`, { value: Math.round(value * 100) })}
    </Badge>
  )
}

function ExtractedField({
  label,
  confidence,
  children,
}: {
  label: string
  confidence: number | null | undefined
  children: ReactNode
}) {
  const low = confidence == null || confidence < 0.8
  return (
    <div className={cn('rounded-lg border p-3', low ? 'border-amber-300/80 bg-amber-50/50 dark:border-amber-800 dark:bg-amber-950/20' : 'border-border')}>
      <div className="mb-2 flex items-center justify-between gap-2">
        <Label>{label}</Label>
        <ConfidenceBadge value={confidence} />
      </div>
      {children}
    </div>
  )
}

function OriginalViewer({ receipt }: { receipt: ReceiptDetail }) {
  const { t } = useTranslation('belege')
  const hasOriginal = Boolean(receipt.archiveId || receipt.inboundDocumentId)
  const original = useReceiptOriginal(receipt.id, hasOriginal)
  const [objectUrl, setObjectUrl] = useState<string | null>(null)

  useEffect(() => {
    if (!original.data) {
      setObjectUrl(null)
      return
    }
    const url = URL.createObjectURL(original.data)
    setObjectUrl(url)
    return () => URL.revokeObjectURL(url)
  }, [original.data])

  const contentType = original.data?.type || receipt.originalContentType || ''
  return (
    <Card className="overflow-hidden lg:sticky lg:top-20 lg:self-start">
      <CardHeader className="border-b border-border bg-muted/35">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle>{t('review.original.title')}</CardTitle>
            <p className="mt-1 text-xs text-muted-foreground">
              {receipt.originalFileName ?? t('review.original.unknownFile')}
            </p>
          </div>
          <Badge variant="outline">{t('review.original.immutable')}</Badge>
        </div>
      </CardHeader>
      <CardContent className="p-0">
        {!hasOriginal && (
          <div className="grid min-h-96 place-items-center p-6 text-center text-sm text-muted-foreground">
            {t('review.original.unavailable')}
          </div>
        )}
        {original.isLoading && (
          <div className="grid min-h-96 place-items-center text-sm text-muted-foreground">{t('review.original.loading')}</div>
        )}
        {original.isError && (
          <div role="alert" className="grid min-h-96 place-items-center p-6 text-center text-sm text-destructive">{t('review.original.error')}</div>
        )}
        {objectUrl && contentType === 'application/pdf' && (
          <iframe title={t('review.original.title')} src={objectUrl} className="h-[68vh] min-h-[520px] w-full bg-muted/20" />
        )}
        {objectUrl && contentType.startsWith('image/') && (
          <div className="flex max-h-[72vh] min-h-[520px] items-start justify-center overflow-auto bg-muted/30 p-4">
            <img src={objectUrl} alt={t('review.original.alt')} className="h-auto max-w-full rounded shadow-sm" />
          </div>
        )}
        {objectUrl && contentType !== 'application/pdf' && !contentType.startsWith('image/') && (
          <div className="grid min-h-96 place-items-center p-6 text-center">
            <p className="text-sm text-muted-foreground">{t('review.original.previewUnsupported')}</p>
          </div>
        )}
        {objectUrl && (
          <div className="border-t border-border p-3">
            <a href={objectUrl} download={receipt.originalFileName ?? 'beleg'} className={cn(buttonVariants({ variant: 'outline', size: 'sm' }), 'w-full')}>
              {t('review.original.download')}
            </a>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function ProposalPreview({ id, currency, enabled, dirty }: { id: string; currency: string; enabled: boolean; dirty: boolean }) {
  const { t } = useTranslation('belege')
  const proposal = useReceiptProposal(id, enabled)

  if (!enabled) {
    return (
      <Card>
        <CardHeader><CardTitle>{t('review.proposal.title')}</CardTitle></CardHeader>
        <CardContent><p className="text-sm text-muted-foreground">{t('review.proposal.reviewFirst')}</p></CardContent>
      </Card>
    )
  }

  return (
    <Card className="overflow-hidden">
      <CardHeader className="border-b border-border">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <CardTitle>{t('review.proposal.title')}</CardTitle>
          {proposal.data && proposal.data.breakdowns.length > 1 && (
            <Badge variant="secondary">{t('review.proposal.multiRate', { count: proposal.data.breakdowns.length })}</Badge>
          )}
        </div>
      </CardHeader>
      <CardContent className="flex flex-col gap-5 pt-4">
        {dirty && <p className="rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100">{t('review.proposal.unsaved')}</p>}
        {proposal.isLoading && <p className="text-sm text-muted-foreground">{t('review.proposal.loading')}</p>}
        {proposal.isError && <p role="alert" className="text-sm text-destructive">{errorDetail(proposal.error) ?? t('review.proposal.error')}</p>}
        {proposal.data && (
          <>
            <dl className="grid gap-3 text-sm sm:grid-cols-3">
              <div><dt className="text-xs text-muted-foreground">{t('review.proposal.supplier')}</dt><dd className="font-medium">{proposal.data.supplier.name ?? '—'}</dd></div>
              <div><dt className="text-xs text-muted-foreground">{t('review.proposal.expenseAccount')}</dt><dd className="font-medium tabular-nums">{proposal.data.expenseAccount}</dd></div>
              <div><dt className="text-xs text-muted-foreground">{t('review.proposal.entryDate')}</dt><dd className="font-medium">{formatDate(proposal.data.entryDate)}</dd></div>
            </dl>

            <div>
              <h3 className="mb-2 text-sm font-semibold">{t('review.proposal.breakdownTitle')}</h3>
              <Table>
                <TableHeader><TableRow><TableHead>{t('review.proposal.rate')}</TableHead><TableHead className="text-right">{t('review.fields.net')}</TableHead><TableHead className="text-right">{t('review.fields.vat')}</TableHead></TableRow></TableHeader>
                <TableBody>
                  {proposal.data.breakdowns.map((row, index) => (
                    <TableRow key={`${row.vatRatePercent}-${index}`}><TableCell>{number.format(row.vatRatePercent)} %</TableCell><TableCell className="text-right">{formatMoney(row.netAmount, currency)}</TableCell><TableCell className="text-right">{formatMoney(row.vatAmount, currency)}</TableCell></TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>

            <div>
              <h3 className="mb-2 text-sm font-semibold">{t('review.proposal.legsTitle')}</h3>
              <Table>
                <TableHeader><TableRow><TableHead>{t('review.proposal.account')}</TableHead><TableHead>{t('review.proposal.direction')}</TableHead><TableHead>{t('review.proposal.rate')}</TableHead><TableHead className="text-right">{t('review.proposal.amount')}</TableHead></TableRow></TableHeader>
                <TableBody>
                  {proposal.data.postingLegs.map((leg, index) => (
                    <TableRow key={`${leg.accountNumber}-${leg.direction}-${index}`}><TableCell className="font-medium tabular-nums">{leg.accountNumber}</TableCell><TableCell>{leg.direction === PostingDirection.Debit ? t('review.proposal.debit') : t('review.proposal.credit')}</TableCell><TableCell>{leg.vatRatePercent == null ? '—' : `${number.format(leg.vatRatePercent)} %`}</TableCell><TableCell className="text-right font-medium">{formatMoney(leg.amount, currency)}</TableCell></TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
            <div className="flex justify-end gap-5 border-t border-border pt-3 text-sm">
              <span>{t('review.fields.net')}: <strong>{formatMoney(proposal.data.totalNet, currency)}</strong></span>
              <span>{t('review.fields.vat')}: <strong>{formatMoney(proposal.data.totalVat, currency)}</strong></span>
              <span>{t('review.fields.gross')}: <strong>{formatMoney(proposal.data.totalGross, currency)}</strong></span>
            </div>
          </>
        )}
      </CardContent>
    </Card>
  )
}

export default function BelegReviewPage() {
  const { t } = useTranslation('belege')
  const { id = '' } = useParams<{ id: string }>()
  const receipt = useReceipt(id)
  const review = useReviewReceipt(id)
  const confirmBook = useConfirmBookReceipt(id)
  const proposalForConfirm = useReceiptProposal(
    id,
    receipt.data?.status === ReceiptStatus.Reviewed,
  )
  const suppliers = useQuery({
    queryKey: ['partners', 'receipt-suppliers'],
    queryFn: () => listPartners({ role: 'supplier', pageSize: 100 }),
  })
  const [draft, setDraft] = useState<ReviewDraft>(EMPTY_DRAFT)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (receipt.data) setDraft(draftFromReceipt(receipt.data))
  }, [receipt.data])

  const dirty = useMemo(
    () => Boolean(receipt.data) && JSON.stringify(draft) !== JSON.stringify(draftFromReceipt(receipt.data!)),
    [draft, receipt.data],
  )

  if (receipt.isLoading) return <main className="app-main"><p className="text-sm text-muted-foreground">{t('review.loading')}</p></main>
  if (receipt.isError || !receipt.data) return <main className="app-main"><p role="alert" className="text-sm text-destructive">{t('review.error')}</p></main>

  const item = receipt.data
  const confidence: ReceiptFieldConfidence = item.fieldConfidence ?? {}
  const reviewable = item.status === ReceiptStatus.Extracted || item.status === ReceiptStatus.Reviewed
  const reviewed = item.status === ReceiptStatus.Reviewed
  const bookedEntryId = confirmBook.data?.journalEntryId ?? item.journalEntryId
  const currency = item.currency ?? 'EUR'
  const selectedSupplierMissing = draft.supplierPartnerId && !suppliers.data?.items.some((partner) => partner.id === draft.supplierPartnerId)

  function update<K extends keyof ReviewDraft>(key: K, value: ReviewDraft[K]) {
    setDraft((current) => ({ ...current, [key]: value }))
    setMessage(null)
    setError(null)
  }

  async function saveReview() {
    setMessage(null)
    setError(null)
    const netAmount = Number(draft.netAmount)
    const vatAmount = Number(draft.vatAmount)
    const grossAmount = Number(draft.grossAmount)
    if (![netAmount, vatAmount, grossAmount].every(Number.isFinite) || (!draft.invoiceDate && !draft.expenseDate) || (!item.inboundDocumentId && !draft.vatRatePercent)) {
      setError(t('review.validation'))
      return
    }
    const body: ReviewReceiptRequest = {
      supplierPartnerId: draft.supplierPartnerId || null,
      expenseAccountOverride: draft.expenseAccountOverride || null,
      vatRatePercent: draft.vatRatePercent === '' ? null : Number(draft.vatRatePercent),
      netAmount,
      vatAmount,
      grossAmount,
      invoiceNumber: draft.invoiceNumber.trim() || null,
      invoiceDate: draft.invoiceDate || null,
      expenseDate: draft.expenseDate || null,
    }
    try {
      await review.mutateAsync(body)
      setMessage(t('review.reviewed'))
    } catch (reviewError) {
      setError(errorDetail(reviewError) ?? t('review.saveError'))
    }
  }

  async function book() {
    setMessage(null)
    setError(null)
    try {
      const result = await confirmBook.mutateAsync()
      setMessage(result.alreadyBooked ? t('review.alreadyBooked') : t('review.booked'))
    } catch (bookError) {
      setError(errorDetail(bookError) ?? t('review.bookError'))
    }
  }

  return (
    <main className="app-main" style={{ maxWidth: '1440px' }}>
      <div className="mb-5 flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="mb-2 flex flex-wrap items-center gap-2">
            <Badge variant="secondary">{t(`status.${item.status}`)}</Badge>
            <Badge variant="outline">{t(`source.${item.source}`)}</Badge>
            {item.source === ReceiptSource.EInvoice && <Badge variant="outline">{t('review.structured')}</Badge>}
          </div>
          <h1 className="text-2xl font-semibold">{item.supplierName ?? item.originalFileName ?? t('review.title')}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t('review.subtitle', { date: formatDate(item.invoiceDate) })}</p>
        </div>
        <Link to="/belege" className={cn(buttonVariants({ variant: 'outline' }))}>{t('actions.backToQueue')}</Link>
      </div>

      {(item.status === ReceiptStatus.Quarantined || item.status === ReceiptStatus.Duplicate || item.status === ReceiptStatus.Captured) && (
        <p className={cn('mb-5 rounded-lg border px-4 py-3 text-sm', item.status === ReceiptStatus.Quarantined ? 'border-destructive/30 bg-destructive/8 text-destructive' : 'border-amber-300 bg-amber-50 text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100')}>
          {t(`review.stateHint.${item.status}`)}
        </p>
      )}

      <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1.08fr)_minmax(420px,0.92fr)]">
        <OriginalViewer receipt={item} />

        <div className="flex min-w-0 flex-col gap-5">
          {bookedEntryId && (
            <Card className="border-primary/30 bg-primary/5">
              <CardContent className="pt-4">
                <p className="text-sm font-semibold text-primary">{t('review.booking.linked')}</p>
                <p className="mt-1 text-sm text-muted-foreground">{t('review.booking.hint')}</p>
                <code className="mt-3 block overflow-x-auto rounded-md bg-card px-3 py-2 text-xs">{bookedEntryId}</code>
              </CardContent>
            </Card>
          )}

          <Card>
            <CardHeader>
              <CardTitle>{t('review.fieldsTitle')}</CardTitle>
              <p className="text-sm text-muted-foreground">{t('review.fieldsHint')}</p>
            </CardHeader>
            <CardContent className="grid gap-3 sm:grid-cols-2">
              <ExtractedField label={t('review.fields.supplier')} confidence={confidence.supplierName}>
                <Select value={draft.supplierPartnerId} disabled={!reviewable || suppliers.isLoading} onChange={(event) => update('supplierPartnerId', event.target.value)}>
                  <option value="">{t('review.fields.noSupplier')}</option>
                  {selectedSupplierMissing && <option value={draft.supplierPartnerId}>{item.supplierName ?? draft.supplierPartnerId}</option>}
                  {suppliers.data?.items.map((partner) => <option key={partner.id} value={partner.id}>{partner.name}</option>)}
                </Select>
                {item.supplierName && <p className="mt-1.5 text-xs text-muted-foreground">{t('review.fields.extractedValue', { value: item.supplierName })}</p>}
              </ExtractedField>

              <ExtractedField label={t('review.fields.supplierVatId')} confidence={confidence.supplierVatId}>
                <Input value={item.supplierVatId ?? ''} readOnly aria-readonly="true" />
              </ExtractedField>

              <ExtractedField label={t('review.fields.invoiceNumber')} confidence={confidence.invoiceNumber}>
                <Input value={draft.invoiceNumber} disabled={!reviewable} onChange={(event) => update('invoiceNumber', event.target.value)} />
              </ExtractedField>

              <ExtractedField label={t('review.fields.invoiceDate')} confidence={confidence.invoiceDate}>
                <Input type="date" value={draft.invoiceDate} disabled={!reviewable} onChange={(event) => update('invoiceDate', event.target.value)} />
              </ExtractedField>

              <div className="rounded-lg border border-border p-3">
                <Label>{t('review.fields.expenseDate')}</Label>
                <Input className="mt-2" type="date" value={draft.expenseDate} disabled={!reviewable} onChange={(event) => update('expenseDate', event.target.value)} />
              </div>

              <div className="rounded-lg border border-border p-3">
                <Label>{t('review.fields.expenseAccount')}</Label>
                <Select className="mt-2" value={draft.expenseAccountOverride} disabled={!reviewable} onChange={(event) => update('expenseAccountOverride', event.target.value)}>
                  <option value="">{t('review.fields.accountAutomatic')}</option>
                  {draft.expenseAccountOverride && !['4980', '6300'].includes(draft.expenseAccountOverride) && <option value={draft.expenseAccountOverride}>{draft.expenseAccountOverride}</option>}
                  <option value="4980">4980 · {t('review.fields.otherExpenseSkr03')}</option>
                  <option value="6300">6300 · {t('review.fields.otherExpenseSkr04')}</option>
                </Select>
              </div>

              <ExtractedField label={t('review.fields.vatRate')} confidence={confidence.vatRatePercent}>
                <Select value={draft.vatRatePercent} disabled={!reviewable} onChange={(event) => update('vatRatePercent', event.target.value)}>
                  {item.inboundDocumentId && <option value="">{t('review.fields.multiRateEinvoice')}</option>}
                  {!item.inboundDocumentId && <option value="" disabled>{t('review.fields.chooseVatRate')}</option>}
                  <option value="19">19 %</option><option value="7">7 %</option><option value="0">0 % · {t('review.fields.taxFree')}</option>
                </Select>
              </ExtractedField>

              <ExtractedField label={t('review.fields.net')} confidence={confidence.netAmount}>
                <Input type="number" min="0" step="0.01" value={draft.netAmount} disabled={!reviewable} onChange={(event) => update('netAmount', event.target.value)} />
              </ExtractedField>
              <ExtractedField label={t('review.fields.vat')} confidence={confidence.vatAmount}>
                <Input type="number" min="0" step="0.01" value={draft.vatAmount} disabled={!reviewable} onChange={(event) => update('vatAmount', event.target.value)} />
              </ExtractedField>
              <ExtractedField label={t('review.fields.gross')} confidence={confidence.grossAmount}>
                <Input type="number" min="0" step="0.01" value={draft.grossAmount} disabled={!reviewable} onChange={(event) => update('grossAmount', event.target.value)} />
              </ExtractedField>
            </CardContent>
          </Card>

          {reviewable && (
            <Button variant="outline" disabled={review.isPending} onClick={() => void saveReview()}>
              {review.isPending ? t('review.saving') : t('review.saveAndPreview')}
            </Button>
          )}

          <ProposalPreview id={id} currency={currency} enabled={reviewed} dirty={dirty} />

          {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
          {message && <p role="status" className="text-sm font-medium text-primary">{message}</p>}

          {reviewed && (
            <Card className="border-primary/25">
              <CardContent className="pt-4">
                <p className="text-sm font-semibold">{t('review.confirm.title')}</p>
                <p className="mt-1 text-sm text-muted-foreground">{t('review.confirm.neverAutomatic')}</p>
                <Button className="mt-4 w-full" size="lg" disabled={dirty || !proposalForConfirm.data || confirmBook.isPending} onClick={() => void book()}>
                  {confirmBook.isPending ? t('review.confirm.booking') : t('review.confirm.action')}
                </Button>
              </CardContent>
            </Card>
          )}
        </div>
      </div>
    </main>
  )
}
