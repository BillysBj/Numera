import { useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  convertSalesDocument,
  createCreditNote,
  downloadDocumentPdf,
  finalizeSalesDocument,
  getSalesDocument,
  parseSnapshot,
  sendDocumentEmail,
  stornoSalesDocument,
  ApiError,
  DocumentStatus,
  DocumentType,
} from '@/lib/api/documents'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button, buttonVariants } from '@/components/ui/button'
import { Badge, type BadgeProps } from '@/components/ui/badge'
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

// Status → shadcn Badge variant (mirrors DocumentListPage).
const STATUS_VARIANT: Record<number, BadgeProps['variant']> = {
  [DocumentStatus.Draft]: 'secondary',
  [DocumentStatus.Finalized]: 'default',
  [DocumentStatus.Sent]: 'default',
  [DocumentStatus.Cancelled]: 'destructive',
  [DocumentStatus.Paid]: 'outline',
}

// TaxCategory ordinal → short code (Platform.Money.TaxCategory declaration order).
const TAX_CODE: Record<number, string> = {
  0: 'S',
  1: 'AE',
  2: 'K',
  3: 'E',
  4: 'Z',
  5: 'G',
  6: 'O',
}

// The chain target types offered by the Convert control — the forward chain only
// (Storno/Gutschrift are correction documents created via their own endpoints).
const CONVERT_TARGETS: DocumentType[] = [
  DocumentType.Angebot,
  DocumentType.Auftragsbestaetigung,
  DocumentType.Lieferschein,
  DocumentType.Rechnung,
]

// DateOnly "yyyy-MM-dd" → localized date without TZ drift.
const dateFmt = new Intl.DateTimeFormat('de-DE')
function formatDate(iso?: string | null): string {
  if (!iso) return '—'
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number)
  if (!y || !m || !d) return iso
  return dateFmt.format(new Date(y, m - 1, d))
}

// Money is DISPLAY-ONLY — never arithmetic on the wire decimals (RESEARCH). One formatter
// per document currency.
function makeMoney(currency: string): (v: number) => string {
  return (v: number) =>
    new Intl.NumberFormat('de-DE', { style: 'currency', currency }).format(v)
}

// --- Frozen §14 snapshot reading (defensive, casing-agnostic) ----------------

function firstStr(
  rec: Record<string, unknown>,
  ...keys: string[]
): string | undefined {
  for (const k of keys) {
    const v = rec[k]
    if (typeof v === 'string' && v.trim()) return v
  }
  return undefined
}

function pickObj(
  rec: Record<string, unknown>,
  keys: string[],
): Record<string, unknown> | undefined {
  for (const k of keys) {
    const v = rec[k]
    if (v && typeof v === 'object') return v as Record<string, unknown>
  }
  return undefined
}

function formatAddress(addr: Record<string, unknown>): string | undefined {
  const street = firstStr(addr, 'Street', 'street')
  const line2 = firstStr(addr, 'Line2', 'line2')
  const postal = firstStr(addr, 'PostalCode', 'postalCode')
  const city = firstStr(addr, 'City', 'city')
  const country = firstStr(addr, 'CountryCode', 'countryCode')
  const cityLine = [postal, city].filter(Boolean).join(' ')
  const parts = [street, line2, cityLine, country].filter(Boolean)
  return parts.length ? parts.join(', ') : undefined
}

interface SnapshotView {
  name?: string
  address?: string
  vatId?: string
  taxNumber?: string
  email?: string
}

function readSnapshot(raw: unknown, isRecipient: boolean): SnapshotView | null {
  const s = parseSnapshot(raw)
  if (!s) return null
  const addr = pickObj(
    s,
    isRecipient
      ? ['BillingAddress', 'billingAddress', 'Address', 'address']
      : ['Address', 'address'],
  )
  return {
    name: firstStr(s, 'LegalName', 'legalName', 'Name', 'name'),
    address: addr ? formatAddress(addr) : undefined,
    vatId: firstStr(s, 'VatId', 'vatId'),
    taxNumber: firstStr(s, 'TaxNumber', 'taxNumber'),
    email: firstStr(s, 'Email', 'email'),
  }
}

// Map a mutation ApiError onto a human message: 422 ValidationProblem lists the missing
// §14 fields; 409/others surface the ProblemDetails detail/title.
function actionErrorMessage(err: unknown): string {
  if (err instanceof ApiError) {
    const body = err.body as
      | { detail?: string; title?: string; errors?: Record<string, string[]> }
      | undefined
    if (body?.errors) {
      const msgs = Object.values(body.errors).flat().filter(Boolean)
      if (msgs.length) return msgs.join(' ')
    }
    if (body?.detail) return body.detail
    if (body?.title) return body.title
    return `Error ${err.status}`
  }
  return err instanceof Error ? err.message : String(err)
}

export default function DocumentDetailPage() {
  const { t } = useTranslation('documents')
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [banner, setBanner] = useState<
    { kind: 'error' | 'success'; text: string } | null
  >(null)
  const [convertTarget, setConvertTarget] = useState<DocumentType>(
    DocumentType.Rechnung,
  )
  const [pdfLang, setPdfLang] = useState<'de' | 'en'>('de')

  const doc = useQuery({
    queryKey: ['document', id],
    queryFn: () => getSalesDocument(id),
  })

  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: ['document', id] })
    queryClient.invalidateQueries({ queryKey: ['documents'] })
  }

  const finalize = useMutation({
    mutationFn: () => finalizeSalesDocument(id),
    onSuccess: (res) => {
      queryClient.setQueryData(['document', id], res)
      queryClient.invalidateQueries({ queryKey: ['documents'] })
      setBanner({
        kind: 'success',
        text: t('detail.finalized', { number: res.documentNumber ?? '' }),
      })
    },
    onError: (err) => setBanner({ kind: 'error', text: actionErrorMessage(err) }),
  })

  const storno = useMutation({
    mutationFn: () => stornoSalesDocument(id),
    onSuccess: (res) => {
      refresh()
      navigate(`/documents/${res.id}`)
    },
    onError: (err) => setBanner({ kind: 'error', text: actionErrorMessage(err) }),
  })

  const creditNote = useMutation({
    mutationFn: () => createCreditNote(id),
    onSuccess: (res) => {
      refresh()
      navigate(`/documents/${res.id}/edit`)
    },
    onError: (err) => setBanner({ kind: 'error', text: actionErrorMessage(err) }),
  })

  const convert = useMutation({
    mutationFn: () => convertSalesDocument(id, convertTarget),
    onSuccess: (res) => {
      queryClient.invalidateQueries({ queryKey: ['documents'] })
      navigate(`/documents/${res.id}/edit`)
    },
    onError: (err) => setBanner({ kind: 'error', text: actionErrorMessage(err) }),
  })

  // Download the stored/rendered §14 PDF (04-03) and trigger a browser save. The filename
  // is the legal document number; fall back gracefully if it is somehow absent.
  const download = useMutation({
    mutationFn: (lang: 'de' | 'en') => downloadDocumentPdf(id, lang),
    onSuccess: (blob) => {
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `${doc.data?.documentNumber?.trim() || 'beleg'}.pdf`
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    },
    onError: (err) => setBanner({ kind: 'error', text: actionErrorMessage(err) }),
  })

  // Enqueue the async e-mail send (04-04). The response is Queued; the detail refresh reflects
  // SentAt once the Hangfire job delivers. 422 (no recipient) / 409 surface in the banner.
  const send = useMutation({
    mutationFn: () => sendDocumentEmail(id),
    onSuccess: () => {
      setBanner({ kind: 'success', text: t('actions.sendQueued') })
      refresh()
    },
    onError: (err) => setBanner({ kind: 'error', text: actionErrorMessage(err) }),
  })

  const busy =
    finalize.isPending ||
    storno.isPending ||
    creditNote.isPending ||
    convert.isPending ||
    download.isPending ||
    send.isPending

  const money = useMemo(
    () => makeMoney(doc.data?.currency ?? 'EUR'),
    [doc.data?.currency],
  )

  if (doc.isLoading) {
    return (
      <main className="app-main">
        <p className="text-muted-foreground">{t('detail.loading')}</p>
      </main>
    )
  }
  if (doc.isError || !doc.data) {
    return (
      <main className="app-main">
        <p className="text-destructive">{t('loadError')}</p>
      </main>
    )
  }

  const d = doc.data
  const isDraft = d.status === DocumentStatus.Draft
  const isCancelled = d.status === DocumentStatus.Cancelled
  const isRechnung = d.documentType === DocumentType.Rechnung
  const canCorrect =
    isRechnung &&
    (d.status === DocumentStatus.Finalized || d.status === DocumentStatus.Sent)
  const title = d.documentNumber?.trim() || t('detail.draftTitle')

  // Send status shown next to the header: 'sent' once SentAt/Status flips (the async job
  // delivered), 'queued' immediately after enqueuing (optimistic, before the job runs).
  const sendStatus: 'sent' | 'queued' | null =
    d.sentAt || d.status === DocumentStatus.Sent
      ? 'sent'
      : send.isPending || send.isSuccess
        ? 'queued'
        : null

  const issuer = readSnapshot(d.issuerSnapshot, false)
  const recipient = readSnapshot(d.recipientSnapshot, true)

  const confirmRun = (msg: string, mutate: () => void) => {
    setBanner(null)
    if (window.confirm(msg)) mutate()
  }

  return (
    <main className="app-main" style={{ maxWidth: '960px' }}>
      {/* Header + action bar */}
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <h1 className="mr-auto flex items-center gap-3 text-2xl font-semibold">
          {t(`type.${d.documentType}`)} {title}
          <Badge variant={STATUS_VARIANT[d.status] ?? 'secondary'}>
            {t(`status.${d.status}`)}
          </Badge>
          {sendStatus && (
            <Badge variant={sendStatus === 'sent' ? 'default' : 'secondary'}>
              {t(`sendStatus.${sendStatus}`)}
            </Badge>
          )}
        </h1>

        <Link
          to="/documents"
          className={cn(buttonVariants({ variant: 'ghost', size: 'sm' }))}
        >
          {t('actions.backToList')}
        </Link>

        {isDraft && (
          <>
            <Link
              to={`/documents/${d.id}/edit`}
              className={cn(buttonVariants({ variant: 'outline', size: 'sm' }))}
            >
              {t('actions.editDraft')}
            </Link>
            <Button
              size="sm"
              disabled={busy}
              onClick={() =>
                confirmRun(t('actions.confirmFinalize'), () => finalize.mutate())
              }
            >
              {finalize.isPending ? t('actions.finalizing') : t('actions.finalize')}
            </Button>
          </>
        )}

        {canCorrect && (
          <>
            <Button
              variant="destructive"
              size="sm"
              disabled={busy}
              onClick={() =>
                confirmRun(t('actions.confirmStorno'), () => storno.mutate())
              }
            >
              {t('actions.storno')}
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={busy}
              onClick={() =>
                confirmRun(t('actions.confirmCreditNote'), () =>
                  creditNote.mutate(),
                )
              }
            >
              {t('actions.creditNote')}
            </Button>
          </>
        )}

        {/* PDF download + e-mail send — only on finalized documents (a Draft has no frozen
            snapshot / render). Language is chosen per download; send uses the frozen recipient. */}
        {!isDraft && (
          <>
            <div className="flex items-center gap-1">
              <Select
                className="h-9 w-[5.5rem]"
                value={pdfLang}
                disabled={busy}
                onChange={(e) => setPdfLang(e.target.value as 'de' | 'en')}
              >
                <option value="de">{t('lang.de')}</option>
                <option value="en">{t('lang.en')}</option>
              </Select>
              <Button
                variant="outline"
                size="sm"
                disabled={busy}
                onClick={() => {
                  setBanner(null)
                  download.mutate(pdfLang)
                }}
              >
                {download.isPending
                  ? t('actions.downloading')
                  : t('actions.downloadPdf')}
              </Button>
            </div>
            {!isCancelled && (
              <Button
                variant="outline"
                size="sm"
                disabled={busy}
                onClick={() =>
                  confirmRun(t('actions.confirmSend'), () => send.mutate())
                }
              >
                {send.isPending ? t('actions.sending') : t('actions.send')}
              </Button>
            )}
          </>
        )}

        {!isCancelled && (
          <div className="flex items-center gap-1">
            <Select
              className="h-9 max-w-[12rem]"
              value={String(convertTarget)}
              disabled={busy}
              onChange={(e) =>
                setConvertTarget(Number(e.target.value) as DocumentType)
              }
            >
              {CONVERT_TARGETS.map((v) => (
                <option key={v} value={v}>
                  {t(`type.${v}`)}
                </option>
              ))}
            </Select>
            <Button
              variant="outline"
              size="sm"
              disabled={busy}
              onClick={() => convert.mutate()}
            >
              {t('actions.convert')}
            </Button>
          </div>
        )}
      </div>

      {banner && (
        <div
          className={cn(
            'mb-4 rounded-md border p-3 text-sm',
            banner.kind === 'error'
              ? 'border-destructive/40 bg-destructive/10 text-destructive'
              : 'border-primary/40 bg-primary/10 text-foreground',
          )}
        >
          {banner.text}
        </div>
      )}

      {isCancelled && (
        <div className="mb-4 rounded-md border border-border bg-muted p-3 text-sm text-muted-foreground">
          {t('detail.notes.cancelled')}
          {d.cancelledByDocumentId && (
            <>
              {' '}
              <Link
                to={`/documents/${d.cancelledByDocumentId}`}
                className="text-primary hover:underline"
              >
                {t('detail.chain.open')}
              </Link>
            </>
          )}
        </div>
      )}

      <div className="grid gap-5 md:grid-cols-2">
        {/* Issuer + Recipient §14 snapshots */}
        <SnapshotCard
          title={t('detail.sections.issuer')}
          view={issuer}
          isDraft={isDraft}
          t={t}
        />
        <SnapshotCard
          title={t('detail.sections.recipient')}
          view={recipient}
          isDraft={isDraft}
          t={t}
        />

        {/* Meta */}
        <Card className="md:col-span-2">
          <CardHeader>
            <CardTitle>{t('detail.sections.meta')}</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm md:grid-cols-3">
            <Field label={t('detail.fields.number')} value={d.documentNumber} />
            <Field
              label={t('detail.fields.date')}
              value={formatDate(d.documentDate)}
            />
            <Field
              label={t('detail.fields.serviceDate')}
              value={d.serviceDate ? formatDate(d.serviceDate) : undefined}
            />
            <Field
              label={t('detail.fields.dueDate')}
              value={d.dueDate ? formatDate(d.dueDate) : undefined}
            />
            <Field
              label={t('detail.fields.buyerReference')}
              value={d.buyerReference}
            />
            <Field
              label={t('detail.fields.finalizedAt')}
              value={
                d.finalizedAt
                  ? new Date(d.finalizedAt).toLocaleString()
                  : undefined
              }
            />
            {d.notes && (
              <div className="col-span-2 md:col-span-3">
                <Field label={t('detail.fields.notes')} value={d.notes} />
              </div>
            )}
          </CardContent>
        </Card>

        {/* Lines */}
        <Card className="md:col-span-2">
          <CardHeader>
            <CardTitle>{t('detail.sections.lines')}</CardTitle>
          </CardHeader>
          <CardContent>
            {d.lines.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                {t('detail.lines.empty')}
              </p>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="w-10">
                      {t('detail.lines.lineNumber')}
                    </TableHead>
                    <TableHead>{t('detail.lines.name')}</TableHead>
                    <TableHead className="text-right">
                      {t('detail.lines.quantity')}
                    </TableHead>
                    <TableHead>{t('detail.lines.unit')}</TableHead>
                    <TableHead className="text-right">
                      {t('detail.lines.netUnitPrice')}
                    </TableHead>
                    <TableHead>{t('detail.lines.taxCategory')}</TableHead>
                    <TableHead className="text-right">
                      {t('detail.lines.vatRatePercent')}
                    </TableHead>
                    <TableHead className="text-right">
                      {t('detail.lines.lineNetAmount')}
                    </TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {d.lines.map((l) => (
                    <TableRow key={l.id}>
                      <TableCell>{l.lineNumber}</TableCell>
                      <TableCell>
                        <div className="font-medium">{l.name}</div>
                        {l.description && (
                          <div className="text-xs text-muted-foreground">
                            {l.description}
                          </div>
                        )}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {l.quantity}
                      </TableCell>
                      <TableCell>{l.unitCode}</TableCell>
                      <TableCell className="text-right tabular-nums">
                        {money(l.netUnitPrice)}
                      </TableCell>
                      <TableCell>{TAX_CODE[l.taxCategory] ?? l.taxCategory}</TableCell>
                      <TableCell className="text-right tabular-nums">
                        {l.vatRatePercent} %
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {money(l.lineNetAmount)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>

        {/* VAT breakdown + totals */}
        <Card className="md:col-span-2">
          <CardHeader>
            <CardTitle>{t('detail.sections.vatBreakdown')}</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {d.taxBreakdown.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                {t('detail.breakdown.empty')}
              </p>
            ) : (
              <>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>{t('detail.breakdown.category')}</TableHead>
                      <TableHead className="text-right">
                        {t('detail.breakdown.rate')}
                      </TableHead>
                      <TableHead className="text-right">
                        {t('detail.breakdown.base')}
                      </TableHead>
                      <TableHead className="text-right">
                        {t('detail.breakdown.tax')}
                      </TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {d.taxBreakdown.map((r) => (
                      <TableRow key={r.id}>
                        <TableCell>
                          {TAX_CODE[r.taxCategory] ?? r.taxCategory}
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {r.vatRatePercent} %
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {money(r.taxableBase)}
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {money(r.taxAmount)}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>

                {/* Mandatory Pflichttexte per exempt/reverse-charge row. */}
                {d.taxBreakdown.some((r) => r.exemptionReasonText) && (
                  <ul className="flex flex-col gap-1 text-xs text-muted-foreground">
                    {d.taxBreakdown
                      .filter((r) => r.exemptionReasonText)
                      .map((r) => (
                        <li key={`ex-${r.id}`}>{r.exemptionReasonText}</li>
                      ))}
                  </ul>
                )}
              </>
            )}

            {d.isKleinunternehmer && (
              <p className="rounded-md bg-muted p-2 text-sm">
                {t('detail.notes.kleinunternehmer')}
              </p>
            )}
            {d.reverseCharge && (
              <p className="rounded-md bg-muted p-2 text-sm">
                {t('detail.notes.reverseCharge')}
              </p>
            )}

            {/* Totals */}
            <div className="ml-auto flex w-full max-w-xs flex-col gap-1 border-t border-border pt-3 text-sm">
              <TotalRow label={t('detail.fields.net')} value={money(d.totalNet)} />
              <TotalRow label={t('detail.fields.tax')} value={money(d.totalTax)} />
              <TotalRow
                label={t('detail.fields.gross')}
                value={money(d.totalGross)}
                strong
              />
              <TotalRow
                label={t('detail.fields.amountDue')}
                value={money(d.amountDue)}
              />
            </div>
          </CardContent>
        </Card>

        {/* Chain links */}
        <Card className="md:col-span-2">
          <CardHeader>
            <CardTitle>{t('detail.sections.chain')}</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-2 text-sm">
            {d.sourceDocumentId || d.correctsDocumentId || d.cancelledByDocumentId ? (
              <>
                <ChainLink
                  label={t('detail.chain.convertedFrom')}
                  to={d.sourceDocumentId}
                  open={t('detail.chain.open')}
                />
                <ChainLink
                  label={t('detail.chain.corrects')}
                  to={d.correctsDocumentId}
                  open={t('detail.chain.open')}
                />
                <ChainLink
                  label={t('detail.chain.cancelledBy')}
                  to={d.cancelledByDocumentId}
                  open={t('detail.chain.open')}
                />
              </>
            ) : (
              <p className="text-muted-foreground">{t('detail.chain.none')}</p>
            )}
          </CardContent>
        </Card>
      </div>
    </main>
  )
}

// --- Local presentational helpers -------------------------------------------

function SnapshotCard({
  title,
  view,
  isDraft,
  t,
}: {
  title: string
  view: SnapshotView | null
  isDraft: boolean
  t: (k: string) => string
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-2 text-sm">
        {view ? (
          <>
            <Field label={t('detail.snapshot.legalName')} value={view.name} />
            <Field label={t('detail.snapshot.address')} value={view.address} />
            <Field label={t('detail.snapshot.vatId')} value={view.vatId} />
            <Field label={t('detail.snapshot.taxNumber')} value={view.taxNumber} />
            {view.email && (
              <Field label={t('detail.snapshot.email')} value={view.email} />
            )}
          </>
        ) : (
          <p className="text-muted-foreground">
            {isDraft ? t('detail.snapshot.pending') : '—'}
          </p>
        )}
      </CardContent>
    </Card>
  )
}

function Field({ label, value }: { label: string; value?: string | null }) {
  return (
    <div className="flex flex-col">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span>{value?.toString().trim() ? value : '—'}</span>
    </div>
  )
}

function TotalRow({
  label,
  value,
  strong,
}: {
  label: string
  value: string
  strong?: boolean
}) {
  return (
    <div className={cn('flex justify-between', strong && 'font-semibold')}>
      <span className={cn(!strong && 'text-muted-foreground')}>{label}</span>
      <span className="tabular-nums">{value}</span>
    </div>
  )
}

function ChainLink({
  label,
  to,
  open,
}: {
  label: string
  to?: string | null
  open: string
}) {
  if (!to) return null
  return (
    <div className="flex items-center justify-between">
      <span className="text-muted-foreground">{label}</span>
      <Link to={`/documents/${to}`} className="text-primary hover:underline">
        {open}
      </Link>
    </div>
  )
}
