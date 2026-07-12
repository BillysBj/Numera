import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import type { ColumnDef, PaginationState } from '@tanstack/react-table'
import {
  listSalesDocuments,
  DocumentStatus,
  DocumentType,
  type SalesDocumentListItem,
} from '@/lib/api/documents'
import { DataTable } from '@/components/DataTable'
import { buttonVariants } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { Badge, type BadgeProps } from '@/components/ui/badge'
import { cn } from '@/lib/utils'

// Gross-total display formatter — EUR, exactly 2 fraction digits. The wire value is a
// decimal serialized as a JSON number; formatted for presentation only (no arithmetic).
const grossFmt = new Intl.NumberFormat('de-DE', {
  style: 'currency',
  currency: 'EUR',
})
const dateFmt = new Intl.DateTimeFormat('de-DE')

function formatDate(iso: string): string {
  // documentDate arrives as an ISO "yyyy-MM-dd" (DateOnly); parse without TZ drift.
  const [y, m, d] = iso.split('-').map(Number)
  if (!y || !m || !d) return iso
  return dateFmt.format(new Date(y, m - 1, d))
}

// Status → shadcn Badge variant.
const STATUS_VARIANT: Record<number, BadgeProps['variant']> = {
  [DocumentStatus.Draft]: 'secondary',
  [DocumentStatus.Finalized]: 'default',
  [DocumentStatus.Sent]: 'default',
  [DocumentStatus.Cancelled]: 'destructive',
  [DocumentStatus.Paid]: 'outline',
}

const TYPE_VALUES = Object.values(DocumentType) as number[]
const STATUS_VALUES = Object.values(DocumentStatus) as number[]

// The list is fully SERVER-SIDE: the shared DataTable runs manualPagination and every
// query-state change (page, size, search, type, status) refetches with `rowCount = total`.
export default function DocumentListPage() {
  const { t } = useTranslation('documents')

  const [pagination, setPagination] = useState<PaginationState>({
    pageIndex: 0,
    pageSize: 25,
  })
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [type, setType] = useState<DocumentType | ''>('')
  const [status, setStatus] = useState<DocumentStatus | ''>('')

  // Debounce the free-text (document-number) search so keystrokes don't hammer the API.
  useEffect(() => {
    const id = setTimeout(() => {
      setDebouncedSearch(search)
      setPagination((p) => ({ ...p, pageIndex: 0 }))
    }, 300)
    return () => clearTimeout(id)
  }, [search])

  const query = useQuery({
    queryKey: [
      'documents',
      {
        page: pagination.pageIndex + 1,
        pageSize: pagination.pageSize,
        q: debouncedSearch,
        type,
        status,
      },
    ],
    queryFn: () =>
      listSalesDocuments({
        page: pagination.pageIndex + 1,
        pageSize: pagination.pageSize,
        q: debouncedSearch,
        type: type === '' ? null : type,
        status: status === '' ? null : status,
      }),
    placeholderData: keepPreviousData,
  })

  const columns = useMemo<ColumnDef<SalesDocumentListItem>[]>(
    () => [
      {
        id: 'type',
        header: t('columns.type'),
        cell: ({ row }) => (
          <Link
            to={`/documents/${row.original.id}`}
            className="font-medium text-primary hover:underline"
          >
            {t(`type.${row.original.documentType}`)}
          </Link>
        ),
      },
      {
        id: 'number',
        header: t('columns.number'),
        cell: ({ row }) => row.original.documentNumber ?? '—',
      },
      {
        id: 'status',
        header: t('columns.status'),
        cell: ({ row }) => (
          <Badge variant={STATUS_VARIANT[row.original.status] ?? 'secondary'}>
            {t(`status.${row.original.status}`)}
          </Badge>
        ),
      },
      {
        id: 'date',
        header: t('columns.date'),
        cell: ({ row }) => formatDate(row.original.documentDate),
      },
      {
        id: 'gross',
        header: t('columns.gross'),
        cell: ({ row }) => (
          <span className="tabular-nums">
            {grossFmt.format(row.original.totalGross)}
          </span>
        ),
      },
      {
        id: 'actions',
        header: t('columns.actions'),
        cell: ({ row }) => (
          <div className="flex justify-end gap-2">
            <Link
              to={`/documents/${row.original.id}`}
              className={cn(buttonVariants({ variant: 'ghost', size: 'sm' }))}
            >
              {t('actions.view')}
            </Link>
            {row.original.status === DocumentStatus.Draft && (
              <Link
                to={`/documents/${row.original.id}/edit`}
                className={cn(buttonVariants({ variant: 'outline', size: 'sm' }))}
              >
                {t('actions.edit')}
              </Link>
            )}
          </div>
        ),
      },
    ],
    [t],
  )

  return (
    <main className="app-main" style={{ maxWidth: '1024px' }}>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">{t('title')}</h1>
        <Link to="/documents/new" className={cn(buttonVariants())}>
          {t('actions.create')}
        </Link>
      </div>

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <Input
          className="max-w-xs"
          placeholder={t('filters.search')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <Select
          className="max-w-[14rem]"
          value={type === '' ? '' : String(type)}
          onChange={(e) => {
            setType(e.target.value === '' ? '' : (Number(e.target.value) as DocumentType))
            setPagination((p) => ({ ...p, pageIndex: 0 }))
          }}
        >
          <option value="">{t('filters.allTypes')}</option>
          {TYPE_VALUES.map((v) => (
            <option key={v} value={v}>
              {t(`type.${v}`)}
            </option>
          ))}
        </Select>
        <Select
          className="max-w-[12rem]"
          value={status === '' ? '' : String(status)}
          onChange={(e) => {
            setStatus(
              e.target.value === '' ? '' : (Number(e.target.value) as DocumentStatus),
            )
            setPagination((p) => ({ ...p, pageIndex: 0 }))
          }}
        >
          <option value="">{t('filters.allStatuses')}</option>
          {STATUS_VALUES.map((v) => (
            <option key={v} value={v}>
              {t(`status.${v}`)}
            </option>
          ))}
        </Select>
      </div>

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
        translationNs="documents"
      />
    </main>
  )
}
