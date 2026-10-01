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
  archivePartner,
  listPartners,
  unarchivePartner,
  type PartnerListItem,
} from '@/lib/api/partners'
import { DataTable } from '@/components/DataTable'
import { Button, buttonVariants } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'

// The list is fully SERVER-SIDE: the shared DataTable runs with manualPagination
// (plus manualSorting/manualFiltering), and every query-state change (page, size,
// search, role, archived) refetches with `rowCount = total` from the API envelope.
export default function PartnerListPage() {
  const { t } = useTranslation('partners')
  const queryClient = useQueryClient()

  const [pagination, setPagination] = useState<PaginationState>({
    pageIndex: 0,
    pageSize: 25,
  })
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [role, setRole] = useState<'' | 'customer' | 'supplier'>('')
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
      'partners',
      {
        page: pagination.pageIndex + 1,
        pageSize: pagination.pageSize,
        q: debouncedSearch,
        role,
        archived,
      },
    ],
    queryFn: () =>
      listPartners({
        page: pagination.pageIndex + 1,
        pageSize: pagination.pageSize,
        q: debouncedSearch,
        role,
        archived,
      }),
    placeholderData: keepPreviousData,
  })

  const archiveMutation = useMutation({
    mutationFn: (p: PartnerListItem) =>
      p.archived ? unarchivePartner(p.id) : archivePartner(p.id),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ['partners'] }),
  })

  const columns = useMemo<ColumnDef<PartnerListItem>[]>(
    () => [
      {
        accessorKey: 'name',
        header: t('columns.name'),
        cell: ({ row }) => (
          <Link
            to={`/partners/${row.original.id}`}
            className="font-medium text-primary hover:underline"
          >
            {row.original.name}
          </Link>
        ),
      },
      {
        id: 'roles',
        header: t('columns.roles'),
        cell: ({ row }) => (
          <div className="flex gap-1">
            {row.original.isCustomer && (
              <Badge variant="secondary">{t('roles.customer')}</Badge>
            )}
            {row.original.isSupplier && (
              <Badge variant="outline">{t('roles.supplier')}</Badge>
            )}
          </div>
        ),
      },
      {
        accessorKey: 'city',
        header: t('columns.city'),
        cell: ({ row }) => row.original.city ?? '—',
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
              to={`/partners/${row.original.id}/edit`}
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
    <main className="app-main app-main--wide">
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">{t('title')}</h1>
        <Link to="/partners/new" className={cn(buttonVariants())}>
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
          className="max-w-40"
          value={role}
          onChange={(e) => {
            setRole(e.target.value as '' | 'customer' | 'supplier')
            setPagination((p) => ({ ...p, pageIndex: 0 }))
          }}
        >
          <option value="">{t('filters.allRoles')}</option>
          <option value="customer">{t('roles.customer')}</option>
          <option value="supplier">{t('roles.supplier')}</option>
        </Select>
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
      />
    </main>
  )
}
