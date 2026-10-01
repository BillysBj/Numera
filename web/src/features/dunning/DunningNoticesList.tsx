import { useMemo, useState } from 'react'
import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'
import type { ColumnDef, PaginationState } from '@tanstack/react-table'
import { DataTable } from '@/components/DataTable'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { downloadDunningNoticePdf, listDunningNotices, type DunningNoticeListItem } from '@/lib/api/dunning'
import { saveBlob } from '@/lib/saveBlob'

const statusLabels: Record<number, string> = { 0: 'Ausstehend', 1: 'Versendet', 2: 'Versand fehlgeschlagen' }
const money = (value: number, currency: string) =>
  new Intl.NumberFormat('de-DE', { style: 'currency', currency }).format(value)

export default function DunningNoticesList() {
  const [pagination, setPagination] = useState<PaginationState>({ pageIndex: 0, pageSize: 25 })
  const query = useQuery({
    queryKey: ['dunning-notices', pagination],
    queryFn: () => listDunningNotices(pagination.pageIndex + 1, pagination.pageSize),
    placeholderData: keepPreviousData,
  })
  const download = useMutation({
    mutationFn: (notice: DunningNoticeListItem) => downloadDunningNoticePdf(notice.id),
    onSuccess: (blob, notice) => saveBlob(blob, `Mahnung-${notice.documentNumber}-${notice.level}.pdf`),
  })
  const columns = useMemo<ColumnDef<DunningNoticeListItem>[]>(() => [
    { accessorKey: 'documentNumber', header: 'Rechnung' },
    { accessorKey: 'recipient', header: 'Empfänger', cell: ({ row }) => row.original.recipient ?? '—' },
    { accessorKey: 'level', header: 'Mahnstufe' },
    { accessorKey: 'issuedOn', header: 'Ausgestellt am', cell: ({ row }) =>
      new Date(`${row.original.issuedOn}T00:00:00`).toLocaleDateString('de-DE') },
    { accessorKey: 'fee', header: 'Mahngebühr', cell: ({ row }) => money(row.original.fee, row.original.currency) },
    { accessorKey: 'totalToPay', header: 'Gesamtbetrag', cell: ({ row }) =>
      <span className="tabular-nums font-medium">{money(row.original.totalToPay, row.original.currency)}</span> },
    { accessorKey: 'status', header: 'Versandstatus', cell: ({ row }) =>
      <Badge variant={row.original.status === 2 ? 'destructive' : 'secondary'}>{statusLabels[row.original.status] ?? 'Unbekannt'}</Badge> },
    { id: 'actions', header: '', cell: ({ row }) =>
      <Button size="sm" variant="outline" disabled={download.isPending}
        aria-label={`Mahnung zu ${row.original.documentNumber} herunterladen`}
        onClick={() => download.mutate(row.original)}>
        {download.isPending && download.variables?.id === row.original.id ? 'Wird geladen …' : 'PDF herunterladen'}
      </Button> },
  ], [download.isPending, download.variables, download.mutate])

  return (
    <section className="mt-8" aria-labelledby="dunning-notices-title">
      <h2 id="dunning-notices-title" className="mb-3 text-xl font-semibold">Mahnungen</h2>
      {query.isError && <p role="alert" className="mb-3 text-sm text-destructive">Mahnungen konnten nicht geladen werden.</p>}
      {download.isError && <p role="alert" className="mb-3 text-sm text-destructive">Die PDF konnte nicht heruntergeladen werden. Bitte versuchen Sie es erneut.</p>}
      <DataTable columns={columns} data={query.data?.items ?? []} rowCount={query.data?.total ?? 0}
        pagination={pagination} onPaginationChange={setPagination} isLoading={query.isLoading}
        emptyMessage="Noch keine Mahnungen vorhanden. Starten Sie einen Mahnlauf für überfällige offene Posten."
        translationNs="openItems" />
    </section>
  )
}
