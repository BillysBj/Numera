import type { ReactNode } from 'react'
import {
  flexRender,
  getCoreRowModel,
  useReactTable,
  type ColumnDef,
  type OnChangeFn,
  type PaginationState,
  type SortingState,
} from '@tanstack/react-table'
import { useTranslation } from 'react-i18next'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

/**
 * Reusable SERVER-SIDE table (shared by the partner and — later — catalog lists).
 * Pagination/sorting/filtering are all `manual`: the parent owns the query state
 * and refetches, and `rowCount` comes from the API `total` so the pager is correct
 * without the client ever holding the full dataset (RESEARCH: server-side paging).
 */
export interface DataTableProps<TData, TValue> {
  columns: ColumnDef<TData, TValue>[]
  data: TData[]
  rowCount: number
  pagination: PaginationState
  onPaginationChange: OnChangeFn<PaginationState>
  sorting?: SortingState
  onSortingChange?: OnChangeFn<SortingState>
  isLoading?: boolean
  emptyMessage?: string
  /**
   * i18n namespace holding the `table.*` pager strings. Defaults to `partners`
   * (the first consumer); the catalog list passes `catalog` so its pager reads the
   * catalog wording. Both namespaces expose the same `table.*` keys.
   */
  translationNs?: string
}

export function DataTable<TData, TValue>({
  columns,
  data,
  rowCount,
  pagination,
  onPaginationChange,
  sorting,
  onSortingChange,
  isLoading,
  emptyMessage,
  translationNs = 'partners',
}: DataTableProps<TData, TValue>) {
  const { t } = useTranslation(translationNs)

  const table = useReactTable({
    data,
    columns,
    rowCount,
    state: { pagination, ...(sorting ? { sorting } : {}) },
    onPaginationChange,
    onSortingChange,
    getCoreRowModel: getCoreRowModel(),
    manualPagination: true,
    manualSorting: true,
    manualFiltering: true,
  })

  const pageCount = table.getPageCount()
  const pageIndex = pagination.pageIndex
  const rows = table.getRowModel().rows

  // Header label per column id, for the mobile card layout ("Label: value").
  const headerLabels = new Map<string, ReactNode>()
  table.getHeaderGroups().forEach((headerGroup) =>
    headerGroup.headers.forEach((header) => {
      if (!header.isPlaceholder) {
        headerLabels.set(
          header.column.id,
          flexRender(header.column.columnDef.header, header.getContext()),
        )
      }
    }),
  )

  return (
    <div className="flex flex-col gap-3">
      {/* Desktop/tablet: the full table (scrolls horizontally if needed). */}
      <div className="hidden rounded-md border border-border md:block">
        <Table>
          <TableHeader>
            {table.getHeaderGroups().map((headerGroup) => (
              <TableRow key={headerGroup.id}>
                {headerGroup.headers.map((header) => (
                  <TableHead key={header.id}>
                    {header.isPlaceholder
                      ? null
                      : flexRender(
                          header.column.columnDef.header,
                          header.getContext(),
                        )}
                  </TableHead>
                ))}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {table.getRowModel().rows.length ? (
              table.getRowModel().rows.map((row) => (
                <TableRow key={row.id}>
                  {row.getVisibleCells().map((cell) => (
                    <TableCell key={cell.id}>
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </TableCell>
                  ))}
                </TableRow>
              ))
            ) : (
              <TableRow>
                <TableCell
                  colSpan={columns.length}
                  className="h-24 text-center text-muted-foreground"
                >
                  {isLoading
                    ? t('table.loading')
                    : (emptyMessage ?? t('table.empty'))}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>

      {/* Mobile: one card per row, each cell as a labelled "Spalte: Wert" line. */}
      <div className="flex flex-col gap-3 md:hidden">
        {rows.length ? (
          rows.map((row) => (
            <div key={row.id} className="rounded-md border border-border p-4">
              <dl className="flex flex-col gap-2">
                {row.getVisibleCells().map((cell) => {
                  const label = headerLabels.get(cell.column.id)
                  const content = flexRender(
                    cell.column.columnDef.cell,
                    cell.getContext(),
                  )
                  return (
                    <div
                      key={cell.id}
                      className={cn(
                        'flex gap-3',
                        label ? 'items-start justify-between' : 'justify-end',
                      )}
                    >
                      {label ? (
                        <dt className="shrink-0 text-xs font-medium text-muted-foreground">
                          {label}
                        </dt>
                      ) : null}
                      <dd
                        className={cn(
                          'min-w-0 text-sm',
                          label ? 'text-right' : 'w-full',
                        )}
                      >
                        {content}
                      </dd>
                    </div>
                  )
                })}
              </dl>
            </div>
          ))
        ) : (
          <div className="rounded-md border border-border p-6 text-center text-sm text-muted-foreground">
            {isLoading ? t('table.loading') : (emptyMessage ?? t('table.empty'))}
          </div>
        )}
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2 text-sm text-muted-foreground">
        <span>{t('table.total', { count: rowCount })}</span>
        <div className="flex items-center gap-2">
          <span>
            {t('table.page', {
              page: pageIndex + 1,
              pages: Math.max(1, pageCount),
            })}
          </span>
          <Button
            variant="outline"
            size="sm"
            onClick={() => table.previousPage()}
            disabled={!table.getCanPreviousPage()}
          >
            {t('table.prev')}
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => table.nextPage()}
            disabled={!table.getCanNextPage()}
          >
            {t('table.next')}
          </Button>
        </div>
      </div>
    </div>
  )
}
