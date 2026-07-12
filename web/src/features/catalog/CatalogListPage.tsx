import { useEffect, useMemo, useState } from 'react'
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
  archiveCatalogItem,
  listCatalogItems,
  unarchiveCatalogItem,
  CatalogItemKind,
  type CatalogListItem,
} from '@/lib/api/catalog'
import { unitLabel } from './units'
import { DataTable } from '@/components/DataTable'
import { Button, buttonVariants } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'

// Net-price display formatter — EUR, exactly 2 fraction digits. The wire value is a
// decimal serialized as a JSON number; we format for presentation only (no arithmetic),
// so there is no float drift in what the user sees.
const priceFmt = new Intl.NumberFormat('de-DE', {
  style: 'currency',
  currency: 'EUR',
})

// The list is fully SERVER-SIDE: the shared DataTable runs manualPagination and every
// query-state change (page, size, search, archived) refetches with `rowCount = total`.
export default function CatalogListPage() {
  const { t } = useTranslation('catalog')
  const queryClient = useQueryClient()

  const [pagination, setPagination] = useState<PaginationState>({
    pageIndex: 0,
    pageSize: 25,
  })
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [archived, setArchived] = useState(false)

  // Debounce the free-text search so keystrokes don't hammer the API.
  useEffect(() => {
    const id = setTimeout(() => {
      setDebouncedSearch(search)
      setPagination((p) => ({ ...p, pageIndex: 0 }))
    }, 300)
    return () => clearTimeout(id)
  }, [search])

  const query = useQuery({
    queryKey: [
      'catalog',
      {
        page: pagination.pageIndex + 1,
        pageSize: pagination.pageSize,
        q: debouncedSearch,
        archived,
      },
    ],
    queryFn: () =>
      listCatalogItems({
        page: pagination.pageIndex + 1,
        pageSize: pagination.pageSize,
        q: debouncedSearch,
        archived,
      }),
    placeholderData: keepPreviousData,
  })

  const archiveMutation = useMutation({
    mutationFn: (c: CatalogListItem) =>
      c.archived ? unarchiveCatalogItem(c.id) : archiveCatalogItem(c.id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['catalog'] }),
  })

  const columns = useMemo<ColumnDef<CatalogListItem>[]>(
    () => [
      {
        accessorKey: 'itemNumber',
        header: t('columns.itemNumber'),
        cell: ({ row }) => (
          <Link
            to={`/catalog/${row.original.id}`}
            className="font-medium text-primary hover:underline"
          >
            {row.original.itemNumber}
          </Link>
        ),
      },
      {
        accessorKey: 'name',
        header: t('columns.name'),
      },
      {
        id: 'kind',
        header: t('columns.kind'),
        cell: ({ row }) => (
          <Badge variant="outline">
            {row.original.kind === CatalogItemKind.Service
              ? t('kind.service')
              : t('kind.product')}
          </Badge>
        ),
      },
      {
        id: 'unit',
        header: t('columns.unit'),
        cell: ({ row }) => unitLabel(row.original.unitCode),
      },
      {
        id: 'netPrice',
        header: t('columns.netPrice'),
        cell: ({ row }) => (
          <span className="tabular-nums">
            {priceFmt.format(row.original.netPrice)}
          </span>
        ),
      },
      {
        id: 'vatRate',
        header: t('columns.vatRate'),
        cell: ({ row }) =>
          row.original.vatRatePercent == null
            ? '—'
            : `${row.original.vatRatePercent} %`,
      },
      {
        id: 'status',
        header: t('columns.status'),
        cell: ({ row }) =>
          row.original.archived ? (
            <Badge variant="destructive">{t('status.archived')}</Badge>
          ) : (
            <Badge variant="secondary">{t('status.active')}</Badge>
          ),
      },
      {
        id: 'actions',
        header: t('columns.actions'),
        cell: ({ row }) => (
          <div className="flex justify-end gap-2">
            <Link
              to={`/catalog/${row.original.id}`}
              className={cn(buttonVariants({ variant: 'ghost', size: 'sm' }))}
            >
              {t('actions.edit')}
            </Link>
            <Button
              variant="outline"
              size="sm"
              disabled={archiveMutation.isPending}
              onClick={() => archiveMutation.mutate(row.original)}
            >
              {row.original.archived
                ? t('actions.unarchive')
                : t('actions.archive')}
            </Button>
          </div>
        ),
      },
    ],
    [t, archiveMutation],
  )

  return (
    <main className="app-main" style={{ maxWidth: '1024px' }}>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">{t('title')}</h1>
        <Link to="/catalog/new" className={cn(buttonVariants())}>
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
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={archived}
            onChange={(e) => {
              setArchived(e.target.checked)
              setPagination((p) => ({ ...p, pageIndex: 0 }))
            }}
          />
          {t('filters.showArchived')}
        </label>
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
        translationNs="catalog"
      />
    </main>
  )
}
