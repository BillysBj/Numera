import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery } from '@tanstack/react-query'
import {
  getInboundDocument,
  downloadInboundOriginal,
  ValidationStatus,
  InboundFormat,
  type InboundFinding,
  type InboundParty,
} from '@/lib/api/inbound'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

const dateFmt = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium' })
function formatDate(iso?: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? iso : dateFmt.format(d)
}

function makeMoney(currency?: string | null) {
  return new Intl.NumberFormat('de-DE', {
    style: 'currency',
    currency: currency || 'EUR',
  })
}

const STATUS_KEYS: Record<ValidationStatus, string> = {
  [ValidationStatus.Accepted]: 'Accepted',
  [ValidationStatus.Rejected]: 'Rejected',
  [ValidationStatus.Unavailable]: 'Unavailable',
}
function statusVariant(
  s: ValidationStatus,
): 'default' | 'secondary' | 'destructive' {
  if (s === ValidationStatus.Accepted) return 'default'
  if (s === ValidationStatus.Rejected) return 'destructive'
  return 'secondary'
}
const FORMAT_KEYS: Record<InboundFormat, string> = {
  [InboundFormat.XmlUbl]: 'XmlUbl',
  [InboundFormat.XmlCii]: 'XmlCii',
  [InboundFormat.ZugferdPdf]: 'ZugferdPdf',
}

// The human-readable inbound e-invoice detail (EINV-04): seller/buyer, dates, totals, the VAT
// breakdown, the KoSIT verdict + explained findings (DE authoritative / EN when switched), the
// matched supplier, and a byte-for-byte original download (GoBD).
export default function InboundDetailPage() {
  const { t, i18n } = useTranslation('inbound')
  const { id = '' } = useParams()
  const [downloadError, setDownloadError] = useState(false)

  const doc = useQuery({
    queryKey: ['inbound-document', id],
    queryFn: () => getInboundDocument(id),
    enabled: !!id,
  })

  const download = useMutation({
    mutationFn: () => downloadInboundOriginal(id),
    onSuccess: (blob) => {
      setDownloadError(false)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = doc.data?.originalFileName || 'original'
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    },
    onError: () => setDownloadError(true),
  })

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
  const rm = d.readModel
  const money = makeMoney(d.currency)

  // Pick the finding explanation for the active language (DE authoritative); fall back to the
  // other language, then the raw KoSIT message.
  const explain = (f: InboundFinding): string =>
    (i18n.language.startsWith('en') ? f.explanationEn : f.explanationDe) ||
    f.explanationDe ||
    f.message

  const renderParty = (p?: InboundParty) => (
    <div className="text-sm">
      <div className="font-medium">{p?.name ?? '—'}</div>
      {(p?.street || p?.city) && (
        <div className="text-muted-foreground">
          {[p?.street, [p?.postalCode, p?.city].filter(Boolean).join(' '), p?.countryCode]
            .filter(Boolean)
            .join(', ')}
        </div>
      )}
      {p?.vatId && (
        <div className="text-muted-foreground">
          {t('detail.vatId')}: {p.vatId}
        </div>
      )}
      {p?.taxNumber && (
        <div className="text-muted-foreground">
          {t('detail.taxNumber')}: {p.taxNumber}
        </div>
      )}
    </div>
  )

  return (
    <main className="app-main" style={{ maxWidth: '960px' }}>
      <div className="mb-4 flex items-center justify-between">
        <div>
          <Link to="/inbound" className="text-sm text-primary hover:underline">
            ← {t('detail.back')}
          </Link>
          <h1 className="mt-1 text-2xl font-semibold">
            {t('detail.title')}
            {d.invoiceNumber ? ` · ${d.invoiceNumber}` : ''}
          </h1>
        </div>
        <div className="flex items-center gap-2">
          <Badge variant="secondary">
            {t(`format.${FORMAT_KEYS[d.detectedFormat]}`)}
          </Badge>
          <Badge variant={statusVariant(d.validationStatus)}>
            {t(`status.${STATUS_KEYS[d.validationStatus]}`)}
          </Badge>
        </div>
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>{t('detail.seller')}</CardTitle>
          </CardHeader>
          <CardContent>{renderParty(rm?.seller)}</CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>{t('detail.buyer')}</CardTitle>
          </CardHeader>
          <CardContent>{renderParty(rm?.buyer)}</CardContent>
        </Card>
      </div>

      <Card className="mt-4">
        <CardHeader>
          <CardTitle>{t('detail.title')}</CardTitle>
        </CardHeader>
        <CardContent>
          <dl className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm sm:grid-cols-4">
            <div>
              <dt className="text-muted-foreground">{t('detail.invoiceNumber')}</dt>
              <dd>{d.invoiceNumber ?? '—'}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t('detail.invoiceDate')}</dt>
              <dd>{formatDate(d.invoiceDate)}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t('detail.currency')}</dt>
              <dd>{d.currency ?? '—'}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t('detail.uploadedAt')}</dt>
              <dd>{formatDate(d.uploadedAt)}</dd>
            </div>
          </dl>
        </CardContent>
      </Card>

      {rm && rm.lines.length > 0 && (
        <Card className="mt-4">
          <CardHeader>
            <CardTitle>{t('detail.lines')}</CardTitle>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t('lineColumns.name')}</TableHead>
                  <TableHead className="text-right">{t('lineColumns.quantity')}</TableHead>
                  <TableHead>{t('lineColumns.unit')}</TableHead>
                  <TableHead className="text-right">{t('lineColumns.unitPrice')}</TableHead>
                  <TableHead className="text-right">{t('lineColumns.net')}</TableHead>
                  <TableHead>{t('lineColumns.category')}</TableHead>
                  <TableHead className="text-right">{t('lineColumns.rate')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rm.lines.map((l, i) => (
                  <TableRow key={i}>
                    <TableCell>{l.name ?? '—'}</TableCell>
                    <TableCell className="text-right tabular-nums">
                      {l.quantity ?? '—'}
                    </TableCell>
                    <TableCell>{l.unitCode ?? '—'}</TableCell>
                    <TableCell className="text-right tabular-nums">
                      {l.netUnitPrice != null ? money.format(l.netUnitPrice) : '—'}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {l.lineNetAmount != null ? money.format(l.lineNetAmount) : '—'}
                    </TableCell>
                    <TableCell>{l.taxCategory ?? '—'}</TableCell>
                    <TableCell className="text-right tabular-nums">
                      {l.vatRatePercent != null ? `${l.vatRatePercent} %` : '—'}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}

      <div className="mt-4 grid gap-4 md:grid-cols-2">
        {rm && rm.breakdownRows.length > 0 && (
          <Card>
            <CardHeader>
              <CardTitle>{t('detail.breakdown')}</CardTitle>
            </CardHeader>
            <CardContent>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t('breakdownColumns.category')}</TableHead>
                    <TableHead className="text-right">{t('breakdownColumns.rate')}</TableHead>
                    <TableHead className="text-right">{t('breakdownColumns.base')}</TableHead>
                    <TableHead className="text-right">{t('breakdownColumns.tax')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rm.breakdownRows.map((b, i) => (
                    <TableRow key={i}>
                      <TableCell>{b.taxCategory ?? '—'}</TableCell>
                      <TableCell className="text-right tabular-nums">
                        {b.vatRatePercent != null ? `${b.vatRatePercent} %` : '—'}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {b.taxableBase != null ? money.format(b.taxableBase) : '—'}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {b.taxAmount != null ? money.format(b.taxAmount) : '—'}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        )}

        <Card>
          <CardHeader>
            <CardTitle>{t('detail.totals')}</CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="grid grid-cols-2 gap-y-2 text-sm">
              <dt className="text-muted-foreground">{t('detail.totalNet')}</dt>
              <dd className="text-right tabular-nums">
                {rm?.totalNet != null ? money.format(rm.totalNet) : '—'}
              </dd>
              <dt className="text-muted-foreground">{t('detail.totalTax')}</dt>
              <dd className="text-right tabular-nums">
                {rm?.totalTax != null ? money.format(rm.totalTax) : '—'}
              </dd>
              <dt className="font-medium">{t('detail.totalGross')}</dt>
              <dd className="text-right font-medium tabular-nums">
                {d.totalGross != null ? money.format(d.totalGross) : '—'}
              </dd>
            </dl>
          </CardContent>
        </Card>
      </div>

      <Card className="mt-4">
        <CardHeader>
          <CardTitle>{t('detail.validation')}</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="mb-3">
            <Badge variant={statusVariant(d.validationStatus)}>
              {t(`status.${STATUS_KEYS[d.validationStatus]}`)}
            </Badge>
          </div>
          {d.findings.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('detail.noFindings')}</p>
          ) : (
            <ul className="flex flex-col gap-2 text-sm">
              {d.findings.map((f, i) => (
                <li key={i} className="flex gap-2">
                  <Badge
                    variant={
                      f.severity.toLowerCase().includes('error') ||
                      f.severity.toLowerCase().includes('fatal')
                        ? 'destructive'
                        : 'secondary'
                    }
                  >
                    {f.ruleId ?? f.severity}
                  </Badge>
                  <span>{explain(f)}</span>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      <Card className="mt-4">
        <CardHeader>
          <CardTitle>{t('detail.supplier')}</CardTitle>
        </CardHeader>
        <CardContent className="flex items-center justify-between">
          {d.matchedPartnerId ? (
            <Link
              to={`/partners/${d.matchedPartnerId}`}
              className="text-primary hover:underline"
            >
              {d.sellerName ?? d.sellerVatId ?? t('detail.supplierLink')}
            </Link>
          ) : (
            <span className="text-muted-foreground">{t('unmatched')}</span>
          )}
          <Button
            variant="outline"
            onClick={() => download.mutate()}
            disabled={download.isPending}
          >
            {t('detail.download')}
          </Button>
        </CardContent>
      </Card>

      {downloadError && (
        <p className="mt-2 text-sm text-destructive">{t('detail.downloadError')}</p>
      )}
    </main>
  )
}
