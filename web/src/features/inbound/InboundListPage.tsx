import { useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
import type { ColumnDef, PaginationState } from '@tanstack/react-table'
import {
  listInboundDocuments,
  uploadInboundDocument,
  ValidationStatus,
  InboundFormat,
  ApiError,
  type InboundListItem,
} from '@/lib/api/inbound'
import { DataTable } from '@/components/DataTable'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'

// EUR amount formatter (display only — money stays server-authoritative).
const amountFmt = new Intl.NumberFormat('de-DE', {
  style: 'currency',
  currency: 'EUR',
})

const dateFmt = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium' })
function formatDate(iso?: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? iso : dateFmt.format(d)
}

// The three KoSIT verdicts → i18n key + Badge variant.
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

// The inbound list (Eingangsbelege): a server-side paged, newest-first list of received
// e-invoices with an upload control. Read-only apart from the upload (EINV-04/EINV-05).
export default function InboundListPage() {
  const { t } = useTranslation('inbound')
  const queryClient = useQueryClient()
  const fileInput = useRef<HTMLInputElement>(null)
  const [banner, setBanner] = useState<{ kind: 'success' | 'error'; text: string } | null>(
    null,
  )

  const [pagination, setPagination] = useState<PaginationState>({
    pageIndex: 0,
    pageSize: 25,
  })

  const query = useQuery({
    queryKey: [
      'inbound-documents',
      { page: pagination.pageIndex + 1, pageSize: pagination.pageSize },
    ],
    queryFn: () =>
      listInboundDocuments({
        page: pagination.pageIndex + 1,
        pageSize: pagination.pageSize,
      }),
    placeholderData: keepPreviousData,
  })

  const upload = useMutation({
    mutationFn: (file: File) => uploadInboundDocument(file),
    onSuccess: () => {
      setBanner({ kind: 'success', text: t('upload.success') })
      setPagination((p) => ({ ...p, pageIndex: 0 }))
      void queryClient.invalidateQueries({ queryKey: ['inbound-documents'] })
    },
    onError: (err) => {
      // 422 → the file was not an e-invoice; other errors → a generic upload failure.
      const text =
        err instanceof ApiError && err.status === 422
          ? t('upload.notEInvoice')
          : t('upload.error')
      setBanner({ kind: 'error', text })
    },
    onSettled: () => {
      if (fileInput.current) fileInput.current.value = ''
    },
  })

  const columns = useMemo<ColumnDef<InboundListItem>[]>(
    () => [
      {
        accessorKey: 'sellerName',
        header: t('columns.seller'),
        cell: ({ row }) => (
          <Link
            to={`/inbound/${row.original.id}`}
            className="font-medium text-primary hover:underline"
          >
            {row.original.sellerName ?? row.original.originalFileName}
          </Link>
        ),
      },
      {
        id: 'invoiceNumber',
        header: t('columns.invoiceNumber'),
        cell: ({ row }) => row.original.invoiceNumber ?? '—',
      },
      {
        id: 'invoiceDate',
        header: t('columns.invoiceDate'),
        cell: ({ row }) => formatDate(row.original.invoiceDate),
      },
      {
        id: 'totalGross',
        header: t('columns.totalGross'),
        cell: ({ row }) => (
          <span className="tabular-nums">
            {row.original.totalGross != null
              ? amountFmt.format(row.original.totalGross)
              : '—'}
          </span>
        ),
      },
      {
        id: 'format',
        header: t('columns.format'),
        cell: ({ row }) => (
          <Badge variant="secondary">
            {t(`format.${FORMAT_KEYS[row.original.detectedFormat]}`)}
          </Badge>
        ),
      },
      {
        id: 'status',
        header: t('columns.status'),
        cell: ({ row }) => (
          <Badge variant={statusVariant(row.original.validationStatus)}>
            {t(`status.${STATUS_KEYS[row.original.validationStatus]}`)}
          </Badge>
        ),
      },
      {
        id: 'supplier',
        header: t('columns.supplier'),
        cell: ({ row }) =>
          row.original.matchedPartnerId ? (
            <Link
              to={`/partners/${row.original.matchedPartnerId}`}
              className="text-primary hover:underline"
            >
              {row.original.sellerName ?? row.original.sellerVatId ?? '—'}
            </Link>
          ) : (
            <span className="text-muted-foreground">{t('unmatched')}</span>
          ),
      },
      {
        id: 'uploadedAt',
        header: t('columns.uploadedAt'),
        cell: ({ row }) => formatDate(row.original.uploadedAt),
      },
    ],
    [t],
  )

  return (
    <main className="app-main app-main--wide">
      <div className="mb-1 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">{t('title')}</h1>
        <div className="flex items-center gap-2">
          <input
            ref={fileInput}
            type="file"
            accept="application/pdf,application/xml,text/xml,.pdf,.xml"
            className="hidden"
            onChange={(e) => {
              const file = e.target.files?.[0]
              if (file) upload.mutate(file)
            }}
          />
          <Button
            onClick={() => fileInput.current?.click()}
            disabled={upload.isPending}
          >
            {upload.isPending ? t('upload.uploading') : t('upload.label')}
          </Button>
        </div>
      </div>
      <p className="mb-4 text-sm text-muted-foreground">{t('subtitle')}</p>

      {banner && (
        <p
          className={`mb-3 text-sm ${
            banner.kind === 'error' ? 'text-destructive' : 'text-primary'
          }`}
        >
          {banner.text}
        </p>
      )}
      {query.isError && (
        <p className="mb-3 text-sm text-destructive">{t('loadError')}</p>
      )}

      <DataTable
        columns={columns}
        data={query.data?.items ?? []}
        rowCount={query.data?.total ?? 0}
        pagination={pagination}
        onPaginationChange={setPagination}
        isLoading={query.isLoading}
        translationNs="inbound"
      />
    </main>
  )
}
