import { useMemo, useState } from 'react'
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
  listOpenItems,
  OpenItemStatus,
  type OpenItemListItem,
} from '@/lib/api/openItems'
import { DataTable } from '@/components/DataTable'
import { Select } from '@/components/ui/select'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import RecordPaymentDialog from '@/features/payments/RecordPaymentDialog'
import { runDunning } from '@/lib/api/dunning'
import { useEntitlements, hasCapability } from '@/lib/entitlements'
import { UpgradeHint } from '@/features/shared/UpgradeHint'
import { usePartnerNames } from '@/features/partners/usePartnerNames'

// Amount display formatter — EUR, exactly 2 fraction digits. The wire value is a decimal
// serialized as a JSON number; formatted for PRESENTATION only (no arithmetic) so there
// is no float drift in what the user sees. Money stays server-authoritative.
const amountFmt = new Intl.NumberFormat('de-DE', {
  style: 'currency',
  currency: 'EUR',
})

// Localised date display for the ISO DateOnly strings (yyyy-MM-dd) the API returns.
const dateFmt = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium' })
function formatDate(iso: string): string {
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? iso : dateFmt.format(d)
}

function daysOverdue(dueDate: string): number {
  const due = new Date(`${dueDate}T00:00:00`)
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  return Math.max(0, Math.floor((today.getTime() - due.getTime()) / 86_400_000))
}

// The four selectable statuses, mapped to their i18n keys. Mirrors OpenItemStatus.
const STATUS_KEYS: Record<OpenItemStatus, string> = {
  [OpenItemStatus.Open]: 'Open',
  [OpenItemStatus.PartiallyPaid]: 'PartiallyPaid',
  [OpenItemStatus.Paid]: 'Paid',
  [OpenItemStatus.Cancelled]: 'Cancelled',
}

// The list is fully SERVER-SIDE: the shared DataTable runs manualPagination and every
// query-state change (page, size, status, overdue-only) refetches with `rowCount = total`.
// Read-only — there is no create/edit here (payments are Phase 6).
export default function OpenItemsListPage() {
  const { t } = useTranslation('openItems')
  const { t: td } = useTranslation('dunning')
  const queryClient = useQueryClient()
  const caps = useEntitlements()
  const canDunning = hasCapability(caps.data?.capabilities, 'Dunning')
  const { nameFor } = usePartnerNames()

  const [pagination, setPagination] = useState<PaginationState>({
    pageIndex: 0,
    pageSize: 25,
  })
  const [status, setStatus] = useState<OpenItemStatus | null>(null)
  const [overdueOnly, setOverdueOnly] = useState(false)
  const [paymentItem, setPaymentItem] = useState<OpenItemListItem | null>(null)
  const [runResult, setRunResult] = useState<string | null>(null)
  const [runError, setRunError] = useState(false)

  const query = useQuery({
    queryKey: [
      'open-items',
      {
        page: pagination.pageIndex + 1,
        pageSize: pagination.pageSize,
        status,
        overdueOnly,
      },
    ],
    queryFn: () =>
      listOpenItems({
        page: pagination.pageIndex + 1,
        pageSize: pagination.pageSize,
        status,
        overdueOnly,
      }),
    placeholderData: keepPreviousData,
  })

  const dunningRun = useMutation({
    mutationFn: runDunning,
    onSuccess: async ({ issued, skipped }) => {
      setRunError(false)
      setRunResult(td('run.result', { issued, skipped }))
      await queryClient.invalidateQueries({ queryKey: ['open-items'] })
    },
    onError: () => {
      setRunResult(null)
      setRunError(true)
    },
  })

  const columns = useMemo<ColumnDef<OpenItemListItem>[]>(
    () => [
      {
        accessorKey: 'documentNumber',
        header: t('columns.documentNumber'),
        cell: ({ row }) => (
          <Link
            to={`/documents/${row.original.documentId}`}
            className="font-medium text-primary hover:underline"
          >
            {row.original.documentNumber}
          </Link>
        ),
      },
      {
        id: 'partner',
        header: t('columns.partner'),
        cell: ({ row }) => nameFor(row.original.partnerId) ?? '—',
      },
      {
        id: 'originalAmount',
        header: t('columns.originalAmount'),
        cell: ({ row }) => (
          <span className="tabular-nums">
            {amountFmt.format(row.original.originalAmount)}
          </span>
        ),
      },
      {
        id: 'openAmount',
        header: t('columns.openAmount'),
        cell: ({ row }) => (
          <span className="tabular-nums font-medium">
            {amountFmt.format(row.original.openAmount)}
          </span>
        ),
      },
      {
        id: 'issuedOn',
        header: t('columns.issuedOn'),
        cell: ({ row }) => formatDate(row.original.issuedOn),
      },
      {
        id: 'dueDate',
        header: t('columns.dueDate'),
        cell: ({ row }) => formatDate(row.original.dueDate),
      },
      {
        id: 'dunningLevel',
        header: td('columns.dunningLevel'),
        cell: ({ row }) => (
          <Badge variant={row.original.currentDunningLevel > 1 ? 'destructive' : 'secondary'}>
            {td(`levelNames.${row.original.currentDunningLevel}`, {
              defaultValue: String(row.original.currentDunningLevel),
            })}
          </Badge>
        ),
      },
      {
        id: 'daysOverdue',
        header: td('columns.daysOverdue'),
        cell: ({ row }) =>
          row.original.overdue ? (
            <Badge variant="destructive">{daysOverdue(row.original.dueDate)}</Badge>
          ) : (
            '—'
          ),
      },
      {
        id: 'status',
        header: t('columns.status'),
        cell: ({ row }) => (
          <div className="flex items-center gap-2">
            <Badge variant="secondary">
              {t(`status.${STATUS_KEYS[row.original.status]}`)}
            </Badge>
            {row.original.overdue && (
              <Badge variant="destructive">{t('overdue')}</Badge>
            )}
          </div>
        ),
      },
      {
        id: 'actions',
        header: '',
        cell: ({ row }) => {
          const canRecord =
            row.original.status === OpenItemStatus.Open ||
            row.original.status === OpenItemStatus.PartiallyPaid
          return (
            <Button
              size="sm"
              variant="outline"
              disabled={!canRecord}
              onClick={() => setPaymentItem(row.original)}
            >
              {t('payments:actions.record')}
            </Button>
          )
        },
      },
    ],
    [t, td, nameFor],
  )

  return (
    <main className="app-main app-main--wide">
      <div className="mb-4 flex items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">{t('title')}</h1>
        <Button
          onClick={() => dunningRun.mutate()}
          disabled={dunningRun.isPending || !canDunning}
        >
          {dunningRun.isPending ? td('run.running') : td('run.button')}
        </Button>
      </div>

      {/* Mahnwesen is plan L+ (server-gated, 09-02): an upgrade hint replaces the raw 403. */}
      {!canDunning && <UpgradeHint requiredTier="L" className="mb-3" />}

      {runResult && <p role="status" className="mb-3 text-sm text-emerald-600">{runResult}</p>}
      {runError && <p role="alert" className="mb-3 text-sm text-destructive">{td('run.error')}</p>}

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <div className="flex flex-col gap-1">
          <label className="text-xs text-muted-foreground">
            {t('filters.status')}
          </label>
          <Select
            className="max-w-xs"
            value={status ?? ''}
            onChange={(e) => {
              const v = e.target.value
              setStatus(v === '' ? null : (Number(v) as OpenItemStatus))
              setPagination((p) => ({ ...p, pageIndex: 0 }))
            }}
          >
            <option value="">{t('filters.allStatuses')}</option>
            {(
              Object.keys(STATUS_KEYS).map(Number) as OpenItemStatus[]
            ).map((s) => (
              <option key={s} value={s}>
                {t(`status.${STATUS_KEYS[s]}`)}
              </option>
            ))}
          </Select>
        </div>
        <label className="mt-4 flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={overdueOnly}
            onChange={(e) => {
              setOverdueOnly(e.target.checked)
              setPagination((p) => ({ ...p, pageIndex: 0 }))
            }}
          />
          {t('filters.overdueOnly')}
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
        translationNs="openItems"
      />
      <RecordPaymentDialog
        open={paymentItem !== null}
        onOpenChange={(open) => {
          if (!open) setPaymentItem(null)
        }}
        openItem={paymentItem}
      />
    </main>
  )
}
