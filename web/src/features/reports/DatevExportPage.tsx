import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { getMe, ApiError } from '@/lib/api'
import { downloadDatev } from '@/lib/api/ledger'
import { hasCapability, useEntitlements } from '@/lib/entitlements'
import { saveBlob } from '@/lib/saveBlob'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { UpgradeHint } from '@/features/shared/UpgradeHint'

export default function DatevExportPage() {
  const today = new Date()
  const localDate = new Date(today.getTime() - today.getTimezoneOffset() * 60_000).toISOString().slice(0, 10)
  const [from, setFrom] = useState(`${today.getFullYear()}-01-01`)
  const [to, setTo] = useState(localDate)
  const me = useQuery({ queryKey: ['me'], queryFn: getMe })
  const caps = useEntitlements()
  const isOwner = me.data?.role === 'Owner'
  const canExport = hasCapability(caps.data?.capabilities, 'DataExport')
  const invalidRange = !from || !to || from > to
  const download = useMutation({
    mutationFn: (range: { from: string; to: string }) => downloadDatev(range.from, range.to),
    onSuccess: (blob, range) => saveBlob(blob, `EXTF_Buchungsstapel_${range.from}_${range.to}.csv`),
  })
  const errorBody = download.error instanceof ApiError ? download.error.body as { detail?: string } | undefined : undefined

  return (
    <main className="app-main">
      <h1 className="text-2xl font-semibold">DATEV-Export</h1>
      <p className="mt-1 text-sm text-muted-foreground">Buchungsstapel für Ihre Steuerberatung herunterladen.</p>
      <p className="mt-3 text-sm text-muted-foreground">Buchungen werden mit Bruttobeträgen und automatischer Umsatzsteuer exportiert. Bitte bestätigen Sie die Konten und BU-Schlüssel mit Ihrer Steuerberatung.</p>
      {me.isSuccess && !isOwner && <p role="status" className="mt-4 text-sm">Nur der Inhaber kann den DATEV-Export herunterladen.</p>}
      {caps.isSuccess && !canExport && <UpgradeHint requiredTier="S" className="mt-4" />}
      {(me.isError || caps.isError) && <p role="alert" className="mt-4 text-sm text-destructive">Berechtigungen konnten nicht geladen werden.</p>}
      <form className="mt-6 rounded-xl border border-border bg-card p-4" onSubmit={(event) => {
        event.preventDefault()
        if (!invalidRange && isOwner && canExport && !download.isPending) download.mutate({ from, to })
      }}>
        <div className="flex flex-wrap items-end gap-3">
          <label className="flex flex-col gap-1.5 text-sm font-medium">Von
            <Input type="date" required value={from} onChange={(event) => setFrom(event.target.value)} />
          </label>
          <label className="flex flex-col gap-1.5 text-sm font-medium">Bis
            <Input type="date" required value={to} min={from} onChange={(event) => setTo(event.target.value)} />
          </label>
          <Button type="submit" disabled={invalidRange || !isOwner || !canExport || download.isPending}>
            {download.isPending ? 'Wird heruntergeladen …' : 'DATEV-Export herunterladen'}
          </Button>
        </div>
        {invalidRange && <p role="alert" className="mt-3 text-sm text-destructive">Bitte wählen Sie einen gültigen Zeitraum: Von darf nicht nach Bis liegen.</p>}
        <p className="mt-3 text-sm text-muted-foreground">Bitte wählen Sie einen Zeitraum innerhalb eines Wirtschaftsjahres. Berater- und Mandantennummer sind mit 0 vorbelegt und müssen beim Import zugeordnet werden.</p>
      </form>
      {download.isError && <p role="alert" className="mt-4 text-sm text-destructive">{errorBody?.detail ?? 'Der DATEV-Export konnte nicht heruntergeladen werden. Bitte versuchen Sie es erneut.'}</p>}
      {download.isSuccess && <p role="status" className="mt-4 text-sm">Der Download wurde gestartet.</p>}
    </main>
  )
}
