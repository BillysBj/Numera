import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef, PaginationState } from '@tanstack/react-table'
import {
  activateTemplate,
  listTemplates,
  pauseTemplate,
  RecurringStatus,
  type RecurringTemplate,
} from '@/lib/api/recurring'
import { DataTable } from '@/components/DataTable'
import { Badge } from '@/components/ui/badge'
import { Button, buttonVariants } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { hasCapability, useEntitlements } from '@/lib/entitlements'
import { UpgradeHint } from '@/features/shared/UpgradeHint'

const dateFmt = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium' })
const formatDate = (value: string) => {
  const date = new Date(`${value.slice(0, 10)}T00:00:00`)
  return Number.isNaN(date.getTime()) ? value : dateFmt.format(date)
}

export default function RecurringTemplateListPage() {
  const { t } = useTranslation('recurring')
  const queryClient = useQueryClient()
  const entitlements = useEntitlements()
  const allowed = hasCapability(
    entitlements.data?.capabilities,
    'RecurringInvoices',
  )
  const [pagination, setPagination] = useState<PaginationState>({
    pageIndex: 0,
    pageSize: 25,
  })
  const [actionError, setActionError] = useState(false)
  const query = useQuery({
    queryKey: ['recurring-templates'],
    queryFn: listTemplates,
    enabled: allowed,
  })
  const statusMutation = useMutation({
    mutationFn: (template: RecurringTemplate) =>
      template.status === RecurringStatus.Active
        ? pauseTemplate(template.id)
        : activateTemplate(template.id),
    onSuccess: async () => {
      setActionError(false)
      await queryClient.invalidateQueries({ queryKey: ['recurring-templates'] })
    },
    onError: () => setActionError(true),
  })
  const columns = useMemo<ColumnDef<RecurringTemplate>[]>(
    () => [
      {
        accessorKey: 'name',
        header: t('columns.name'),
        cell: ({ row }) => (
          <Link
            to={`/recurring/${row.original.id}/edit`}
            className="font-medium text-primary hover:underline"
          >
            {row.original.name}
          </Link>
        ),
      },
      {
        accessorKey: 'partnerId',
        header: t('columns.partner'),
        cell: ({ row }) => row.original.partnerId ?? '—',
      },
      {
        id: 'cadence',
        header: t('columns.cadence'),
        cell: ({ row }) =>
          t('cadence', {
            count: row.original.intervalCount,
            unit: t(`interval.${row.original.intervalUnit}`),
          }),
      },
      {
        accessorKey: 'nextRunOn',
        header: t('columns.nextRun'),
        cell: ({ row }) => formatDate(row.original.nextRunOn),
      },
      {
        accessorKey: 'status',
        header: t('columns.status'),
        cell: ({ row }) => (
          <Badge
            variant={
              row.original.status === RecurringStatus.Active
                ? 'default'
                : 'secondary'
            }
          >
            {t(`status.${row.original.status}`)}
          </Badge>
        ),
      },
      {
        accessorKey: 'generatedCount',
        header: t('columns.generatedCount'),
      },
      {
        id: 'actions',
        header: '',
        cell: ({ row }) =>
          row.original.status === RecurringStatus.Ended ? null : (
            <Button
              size="sm"
              variant="outline"
              disabled={statusMutation.isPending}
              onClick={() => statusMutation.mutate(row.original)}
            >
              {row.original.status === RecurringStatus.Active
                ? t('actions.pause')
                : t('actions.activate')}
            </Button>
          ),
      },
    ],
    [statusMutation, t],
  )

  if (entitlements.isLoading) {
    return <main className="app-main">{t('loading')}</main>
  }
  if (!allowed) {
    return (
      <main className="app-main">
        <h1 className="mb-4 text-2xl font-semibold">{t('title')}</h1>
        <UpgradeHint />
      </main>
    )
  }

  const items = query.data ?? []
  const pageItems = items.slice(
    pagination.pageIndex * pagination.pageSize,
    (pagination.pageIndex + 1) * pagination.pageSize,
  )
  return (
    <main className="app-main" style={{ maxWidth: '1200px' }}>
      <div className="mb-4 flex items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">{t('title')}</h1>
        <Link
          to="/recurring/new"
          className={cn(buttonVariants({ variant: 'default' }))}
        >
          {t('actions.new')}
        </Link>
      </div>
      {query.isError && <p role="alert" className="mb-3 text-destructive">{t('loadError')}</p>}
      {actionError && <p role="alert" className="mb-3 text-destructive">{t('actionError')}</p>}
      <DataTable
        columns={columns}
        data={pageItems}
        rowCount={items.length}
        pagination={pagination}
        onPaginationChange={setPagination}
        isLoading={query.isLoading}
        translationNs="recurring"
      />
    </main>
  )
}
